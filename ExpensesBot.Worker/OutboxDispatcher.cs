using ExpensesBot.Data;
using ExpensesBot.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpensesBot.Worker;

/// <summary>
/// Delivers <see cref="OutboxMessage"/> rows to Telegram. Runs next to the bot's polling
/// loop, so a slow or failing Telegram never blocks message handling, and retries
/// transient failures with exponential backoff (or Telegram's <c>retry_after</c>).
/// </summary>
public sealed class OutboxDispatcher(
    ILogger<OutboxDispatcher> logger,
    IServiceProvider serviceProvider,
    IConfiguration configuration,
    TelegramSender sender) : BackgroundService
{
    public const int MaxAttempts = 8;
    public const int BatchSize = 20;
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(1);

    // Telegram allows about 30 messages per second across all chats.
    private static readonly TimeSpan DelayBetweenMessages = TimeSpan.FromMilliseconds(50);

    private DateTime _lastPruneUtc = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var botToken = TelegramBotToken.Get(configuration);
        if (string.IsNullOrWhiteSpace(botToken))
        {
            logger.LogWarning("Telegram bot token is not configured: outbox messages will stay pending");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();

                var processed = await DispatchDueAsync(
                    db,
                    async (chatId, text, ct) =>
                    {
                        var result = await sender.SendTextAsync(botToken, chatId, text, ct);
                        await Task.Delay(DelayBetweenMessages, ct);
                        return result;
                    },
                    DateTime.UtcNow,
                    logger,
                    stoppingToken);

                if (DateTime.UtcNow - _lastPruneUtc >= PruneInterval)
                {
                    await PruneAsync(db, DateTime.UtcNow, stoppingToken);
                    _lastPruneUtc = DateTime.UtcNow;
                }

                // A full batch means more may be waiting: continue right away.
                if (processed < BatchSize)
                    await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox dispatch failed");
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    /// <summary>
    /// Sends one batch of due messages and records each outcome right after sending,
    /// so a crash mid-batch re-sends at most the message in flight.
    /// </summary>
    /// <returns>How many messages were attempted.</returns>
    public static async Task<int> DispatchDueAsync(
        ExpensesDbContext db,
        Func<long, string, CancellationToken, Task<DeliveryResult>> send,
        DateTime nowUtc,
        ILogger logger,
        CancellationToken ct)
    {
        var due = await db.OutboxMessages
            .Include(x => x.User)
            .Where(x => x.Status == OutboxStatus.Pending && x.NextAttemptAtUtc <= nowUtc)
            .OrderBy(x => x.NextAttemptAtUtc)
            .ThenBy(x => x.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var message in due)
        {
            var result = await send(message.User.TelegramUserId, message.Text, ct);
            Apply(message, result, nowUtc);
            await db.SaveChangesAsync(ct);

            switch (message.Status)
            {
                case OutboxStatus.Sent:
                    logger.LogInformation("Delivered {Kind} #{MessageId} to user {UserId}", message.Kind, message.Id, message.UserId);
                    break;
                case OutboxStatus.Failed:
                    logger.LogWarning("Gave up on {Kind} #{MessageId} after {Attempts} attempts: {Error}",
                        message.Kind, message.Id, message.Attempts, message.LastError);
                    break;
                default:
                    logger.LogInformation("Will retry {Kind} #{MessageId} at {NextAttemptAt:o}: {Error}",
                        message.Kind, message.Id, message.NextAttemptAtUtc, message.LastError);
                    break;
            }
        }

        return due.Count;
    }

    /// <summary>Moves a message to its next state after a delivery attempt.</summary>
    public static void Apply(OutboxMessage message, DeliveryResult result, DateTime nowUtc)
    {
        message.Attempts++;
        message.LastError = result.Error is { Length: > OutboxMessage.MaxErrorLength } error
            ? error[..OutboxMessage.MaxErrorLength]
            : result.Error;

        switch (result.Status)
        {
            case DeliveryStatus.Delivered:
                message.Status = OutboxStatus.Sent;
                message.SentAtUtc = nowUtc;
                break;
            case DeliveryStatus.Rejected:
                message.Status = OutboxStatus.Failed;
                break;
            case DeliveryStatus.RetryLater when message.Attempts >= MaxAttempts:
                message.Status = OutboxStatus.Failed;
                break;
            default:
                message.NextAttemptAtUtc = nowUtc + (result.RetryAfter ?? Backoff(message.Attempts));
                break;
        }
    }

    /// <summary>30 s, 1 min, 2 min, 4 min… capped at one hour.</summary>
    public static TimeSpan Backoff(int attempts)
    {
        var seconds = 30 * Math.Pow(2, Math.Max(attempts - 1, 0));
        return seconds >= MaxBackoff.TotalSeconds ? MaxBackoff : TimeSpan.FromSeconds(seconds);
    }

    /// <summary>Deletes delivered and abandoned messages older than <see cref="Retention"/>.</summary>
    public static Task<int> PruneAsync(ExpensesDbContext db, DateTime nowUtc, CancellationToken ct)
    {
        var cutoff = nowUtc - Retention;

        return db.OutboxMessages
            .Where(x => x.Status != OutboxStatus.Pending && x.CreatedAtUtc < cutoff)
            .ExecuteDeleteAsync(ct);
    }
}
