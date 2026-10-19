using ExpensesBot.Data;
using ExpensesBot.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpensesBot.Worker;

/// <summary>
/// Remembers in the database which scheduled notifications were queued, so a
/// restart in the middle of the reminder hour (or a second Worker instance)
/// does not queue the same message again. The log row and the outbox message
/// are saved together: either both exist or neither does.
/// </summary>
public sealed class NotificationLedger(ExpensesDbContext db)
{
    /// <summary>How long reminder and weekly-report entries are kept.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    public static string DateKey(DateTime dateUtc) => NotificationLog.DateKey(dateUtc);

    public static string BudgetKey(long budgetId) => NotificationLog.BudgetKey(budgetId);

    public Task<bool> WasSentAsync(long userId, NotificationKind kind, string key, CancellationToken ct) =>
        db.NotificationLogs.AnyAsync(x => x.UserId == userId && x.Kind == kind && x.Key == key, ct);

    /// <summary>Queues <paramref name="message"/> unless this notification was queued before.</summary>
    /// <returns>False when another run (or instance) already queued it.</returns>
    public async Task<bool> EnqueueOnceAsync(
        long userId,
        NotificationKind kind,
        string key,
        OutboxMessage message,
        CancellationToken ct)
    {
        var entry = new NotificationLog
        {
            UserId = userId,
            Kind = kind,
            Key = key,
            SentAtUtc = message.CreatedAtUtc
        };

        db.NotificationLogs.Add(entry);
        db.OutboxMessages.Add(message);

        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // The unique index says another instance queued it first: nothing to do.
            return false;
        }
        finally
        {
            db.Entry(entry).State = EntityState.Detached;
            db.Entry(message).State = EntityState.Detached;
        }
    }

    /// <summary>
    /// Deletes old reminder and report entries. Budget alerts are kept: a budget
    /// alerts once in its lifetime, and the row disappears with the user.
    /// </summary>
    public Task<int> PruneAsync(DateTime nowUtc, CancellationToken ct)
    {
        var cutoff = nowUtc - Retention;

        return db.NotificationLogs
            .Where(x => x.Kind != NotificationKind.BudgetAlert && x.SentAtUtc < cutoff)
            .ExecuteDeleteAsync(ct);
    }
}
