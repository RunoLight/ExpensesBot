using System.Globalization;
using ExpensesBot.Data;
using ExpensesBot.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpensesBot.Worker;

/// <summary>A transaction as the bot shows it when offering or confirming an undo.</summary>
public sealed record UndoCandidate(
    long Id,
    TransactionType Type,
    long AmountMinor,
    string Currency,
    string? CategoryName,
    string? Note,
    DateTime OccurredAtUtc)
{
    public string Describe(BotText text) =>
        $"{(Type == TransactionType.Income ? "➕" : "➖")} {text.Money(AmountMinor, Currency)}" +
        (CategoryName is not null ? $" · {CategoryName}" : "") +
        (Note is not null ? $" · {Note}" : "") +
        $" · {text.Date(OccurredAtUtc)}";
}

/// <summary>
/// "Undo" for the chat: the ↩️ button under a recorded entry and the /undo
/// command. Every lookup is scoped to the Telegram user who pressed the
/// button, so an id taken from callback_data can't touch someone else's data.
/// </summary>
public static class BotUndo
{
    public const string Prefix = "undo:";

    public static string BuildCallback(long transactionId) =>
        Prefix + transactionId.ToString(CultureInfo.InvariantCulture);

    public static bool TryParseCallback(string data, out long transactionId)
    {
        transactionId = 0;
        return data.StartsWith(Prefix, StringComparison.Ordinal) &&
               long.TryParse(data[Prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out transactionId) &&
               transactionId > 0;
    }

    /// <summary>The user's most recently created transaction, from the bot or the app.</summary>
    public static Task<UndoCandidate?> FindLastAsync(ExpensesDbContext db, long telegramUserId, CancellationToken ct) =>
        Project(OwnedBy(db, telegramUserId)
                .OrderByDescending(t => t.Id))
            .FirstOrDefaultAsync(ct);

    /// <summary>Deletes the transaction if it belongs to the user; returns what was deleted.</summary>
    public static async Task<UndoCandidate?> DeleteAsync(
        ExpensesDbContext db,
        long telegramUserId,
        long transactionId,
        CancellationToken ct)
    {
        // Filter on the entity, then project: EF can't filter on members of a
        // record built with a constructor.
        var candidate = await Project(OwnedBy(db, telegramUserId)
                .Where(t => t.Id == transactionId))
            .FirstOrDefaultAsync(ct);

        if (candidate is null)
            return null;

        var deleted = await db.Transactions
            .Where(t => t.Id == candidate.Id)
            .ExecuteDeleteAsync(ct);

        return deleted > 0 ? candidate : null;
    }

    private static IQueryable<Transaction> OwnedBy(ExpensesDbContext db, long telegramUserId) =>
        db.Transactions
            .AsNoTracking()
            .Where(t => t.User.TelegramUserId == telegramUserId);

    private static IQueryable<UndoCandidate> Project(IQueryable<Transaction> query) =>
        query.Select(t => new UndoCandidate(
            t.Id,
            t.Type,
            t.AmountMinor,
            t.Currency,
            t.Category != null ? t.Category.Name : null,
            t.Note,
            t.OccurredAtUtc));
}
