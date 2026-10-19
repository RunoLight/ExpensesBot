using ExpensesBot.Data;
using ExpensesBot.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace ExpensesBot.Worker;

/// <summary>
/// Background loop: Telegram long polling plus periodic maintenance
/// (recurring transactions, notifications, exchange rates).
/// </summary>
public class Worker(
    ILogger<Worker> logger,
    IServiceProvider serviceProvider,
    IConfiguration configuration) : BackgroundService
{
    private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DelayBetweenMessages = TimeSpan.FromMilliseconds(100);
    private const int LongPollTimeoutSeconds = 25;
    private const string NoCategoryName = "Без категории";

    private long _lastUpdateId;
    private DateTime _lastMaintenanceUtc = DateTime.MinValue;

    private int DailyReminderHourUtc =>
        configuration.GetValue("Telegram:DailyReminderHourUtc", 17);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker started");

        var botToken = TelegramBotToken.Get(configuration);
        if (string.IsNullOrWhiteSpace(botToken))
        {
            logger.LogWarning(
                "Telegram bot token is not configured (Telegram:BotToken or TELEGRAM_BOT_TOKEN). " +
                "Only recurring transactions and exchange rates will be processed.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTime.UtcNow - _lastMaintenanceUtc >= MaintenanceInterval)
                {
                    await RunMaintenanceAsync(botToken, stoppingToken);
                    _lastMaintenanceUtc = DateTime.UtcNow;
                }

                if (string.IsNullOrWhiteSpace(botToken))
                {
                    await Task.Delay(MaintenanceInterval, stoppingToken);
                }
                else if (!await PollTelegramUpdatesAsync(botToken, stoppingToken))
                {
                    await Task.Delay(RetryDelay, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during worker cycle");
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }

        logger.LogInformation("Worker stopped");
    }

    private async Task RunMaintenanceAsync(string? botToken, CancellationToken ct)
    {
        await ProcessRecurringTransactionsAsync(ct);

        await serviceProvider
            .GetRequiredService<ExchangeRateService>()
            .UpdateRatesAsync(ct);

        if (!string.IsNullOrWhiteSpace(botToken))
            await SendScheduledNotificationsAsync(botToken, ct);
    }

    /// <summary>
    /// Long polling: Telegram holds the request open for up to
    /// <see cref="LongPollTimeoutSeconds"/> seconds and answers as soon as an
    /// update arrives, so messages are handled without extra delay.
    /// </summary>
    private async Task<bool> PollTelegramUpdatesAsync(string botToken, CancellationToken ct)
    {
        try
        {
            var http = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient();
            var url = $"https://api.telegram.org/bot{botToken}/getUpdates" +
                      $"?offset={_lastUpdateId + 1}&timeout={LongPollTimeoutSeconds}";

            var json = JObject.Parse(await http.GetStringAsync(url, ct));

            if (json["ok"]?.Value<bool>() != true || json["result"] is not JArray results)
            {
                logger.LogWarning("Telegram getUpdates returned an error: {Response}", json.ToString());
                return false;
            }

            var handler = serviceProvider.GetRequiredService<TelegramBotHandler>();

            foreach (var result in results.OfType<JObject>())
            {
                _lastUpdateId = result["update_id"]?.Value<long>() ?? _lastUpdateId;
                await handler.ProcessUpdateAsync(botToken, result, ct);
            }

            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error polling Telegram updates");
            return false;
        }
    }

    private async Task ProcessRecurringTransactionsAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();

        var now = DateTime.UtcNow;

        var recurring = await db.RecurringTransactions
            .Where(x => x.IsActive && x.LastExecutedAtUtc < now)
            .ToListAsync(ct);

        var created = 0;

        foreach (var rt in recurring)
        {
            if (rt.EndsAfter.HasValue && rt.EndsAfter.Value <= now)
            {
                rt.IsActive = false;
                continue;
            }

            if (!RecurrenceSchedule.IsDue(rt.Pattern, rt.LastExecutedAtUtc, now))
                continue;

            db.Transactions.Add(new Transaction
            {
                UserId = rt.UserId,
                CategoryId = rt.CategoryId,
                AmountMinor = rt.AmountMinor,
                Currency = rt.Currency,
                Type = rt.Type,
                Note = RecurrenceSchedule.AutoNote(rt),
                OccurredAtUtc = now,
                CreatedAtUtc = now
            });

            rt.TimesExecuted++;
            rt.LastExecutedAtUtc = now;
            created++;
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync(ct);

        if (created > 0)
            logger.LogInformation("Created {Count} transactions from recurring rules", created);
    }

    /// <summary>
    /// Sends the evening reminder, the Sunday report and budget alerts. What was
    /// already sent is stored in the database (<see cref="NotificationLedger"/>),
    /// so this can run every few minutes and survive restarts without duplicates.
    /// </summary>
    private async Task SendScheduledNotificationsAsync(string botToken, CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
        var ledger = new NotificationLedger(db);

        var now = DateTime.UtcNow;
        var today = now.Date;
        var tomorrow = today.AddDays(1);
        var todayKey = NotificationLedger.DateKey(today);
        var activeSince = now.AddDays(-7);
        var isReminderHour = now.Hour == DailyReminderHourUtc;
        var isReportDay = now.DayOfWeek == DayOfWeek.Sunday;

        if (isReminderHour)
        {
            // Users who recorded something during the last week.
            var activeUsers = await db.Users
                .AsNoTracking()
                .Where(u => u.Transactions.Any(t => t.CreatedAtUtc >= activeSince))
                .ToListAsync(ct);

            foreach (var user in activeUsers)
            {
                if (await ledger.WasSentAsync(user.Id, NotificationKind.DailyReminder, todayKey, ct))
                    continue;

                var spentMinor = await SumExpensesAsync(db, user, today, tomorrow, ct);

                var settled = await notifications.SendDailyReminderAsync(
                    botToken,
                    user.TelegramUserId,
                    user.DisplayName ?? "друг",
                    spentMinor,
                    user.DefaultCurrency,
                    ct);

                if (settled)
                    await ledger.MarkSentAsync(user.Id, NotificationKind.DailyReminder, todayKey, now, ct);

                await Task.Delay(DelayBetweenMessages, ct);
            }

            if (isReportDay)
            {
                var weekStart = tomorrow.AddDays(-7);

                foreach (var user in activeUsers)
                {
                    if (await ledger.WasSentAsync(user.Id, NotificationKind.WeeklyReport, todayKey, ct))
                        continue;

                    var spentMinor = await SumExpensesAsync(db, user, weekStart, tomorrow, ct);
                    if (spentMinor <= 0)
                        continue;

                    var topCategories = await db.Transactions
                        .Where(t => t.UserId == user.Id &&
                                    t.Type == TransactionType.Expense &&
                                    t.Currency == user.DefaultCurrency &&
                                    t.OccurredAtUtc >= weekStart &&
                                    t.OccurredAtUtc < tomorrow)
                        .GroupBy(t => t.Category != null ? t.Category.Name : NoCategoryName)
                        .Select(g => new { Name = g.Key, Total = g.Sum(t => t.AmountMinor) })
                        .OrderByDescending(x => x.Total)
                        .Take(5)
                        .ToListAsync(ct);

                    var settled = await notifications.SendWeeklyReportAsync(
                        botToken,
                        user.TelegramUserId,
                        user.DisplayName ?? "друг",
                        spentMinor,
                        user.DefaultCurrency,
                        topCategories.Select(x => (x.Name, x.Total)).ToList(),
                        ct);

                    if (settled)
                        await ledger.MarkSentAsync(user.Id, NotificationKind.WeeklyReport, todayKey, now, ct);

                    await Task.Delay(DelayBetweenMessages, ct);
                }
            }

            await ledger.PruneAsync(now, ct);
        }

        await SendBudgetAlertsAsync(db, notifications, ledger, botToken, now, ct);
    }

    private static async Task SendBudgetAlertsAsync(
        ExpensesDbContext db,
        NotificationService notifications,
        NotificationLedger ledger,
        string botToken,
        DateTime now,
        CancellationToken ct)
    {
        var activeBudgets = await db.Budgets
            .AsNoTracking()
            .Include(x => x.User)
            .Include(x => x.Category)
            .Where(x => x.IsActive && x.StartDateUtc <= now && x.EndDateUtc >= now)
            .ToListAsync(ct);

        foreach (var budget in activeBudgets)
        {
            var key = NotificationLedger.BudgetKey(budget.Id);
            if (await ledger.WasSentAsync(budget.UserId, NotificationKind.BudgetAlert, key, ct))
                continue;

            var spentMinor = await db.Transactions
                .Where(t => t.UserId == budget.UserId &&
                            t.Type == TransactionType.Expense &&
                            t.Currency == budget.Currency &&
                            t.OccurredAtUtc >= budget.StartDateUtc &&
                            t.OccurredAtUtc <= budget.EndDateUtc &&
                            (!budget.CategoryId.HasValue || t.CategoryId == budget.CategoryId))
                .SumAsync(t => t.AmountMinor, ct);

            if (budget.AmountMinor <= 0 || spentMinor < budget.AmountMinor * 0.9m)
                continue;

            var settled = await notifications.SendBudgetAlertAsync(
                botToken,
                budget.User.TelegramUserId,
                budget.Category?.Name ?? "Общий",
                spentMinor,
                budget.AmountMinor,
                budget.Currency,
                ct);

            if (settled)
                await ledger.MarkSentAsync(budget.UserId, NotificationKind.BudgetAlert, key, now, ct);
        }
    }

    private static Task<long> SumExpensesAsync(
        ExpensesDbContext db,
        User user,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct) =>
        db.Transactions
            .Where(t => t.UserId == user.Id &&
                        t.Type == TransactionType.Expense &&
                        t.Currency == user.DefaultCurrency &&
                        t.OccurredAtUtc >= fromUtc &&
                        t.OccurredAtUtc < toUtc)
            .SumAsync(t => t.AmountMinor, ct);
}
