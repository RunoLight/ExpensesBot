using ExpensesBot.Data;
using ExpensesBot.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpensesBot.Worker;

/// <summary>
/// Remembers in the database which scheduled notifications were sent, so a
/// restart in the middle of the reminder hour (or a second Worker instance)
/// does not send the same message again.
/// </summary>
public sealed class NotificationLedger(ExpensesDbContext db)
{
    /// <summary>How long reminder and weekly-report entries are kept.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    public static string DateKey(DateTime dateUtc) => NotificationLog.DateKey(dateUtc);

    public static string BudgetKey(long budgetId) => NotificationLog.BudgetKey(budgetId);

    public Task<bool> WasSentAsync(long userId, NotificationKind kind, string key, CancellationToken ct) =>
        db.NotificationLogs.AnyAsync(x => x.UserId == userId && x.Kind == kind && x.Key == key, ct);

    public async Task MarkSentAsync(long userId, NotificationKind kind, string key, DateTime sentAtUtc, CancellationToken ct)
    {
        var entry = new NotificationLog
        {
            UserId = userId,
            Kind = kind,
            Key = key,
            SentAtUtc = sentAtUtc
        };

        db.NotificationLogs.Add(entry);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The unique index says another instance recorded it first: nothing to do.
        }
        finally
        {
            db.Entry(entry).State = EntityState.Detached;
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
