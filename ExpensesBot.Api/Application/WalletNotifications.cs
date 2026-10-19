using ExpensesBot.Data;
using ExpensesBot.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpensesBot.Api.Application;

/// <summary>
/// Tells the other members of a shared wallet about a new transaction. Messages are
/// added to the outbox in the caller's unit of work, so they are committed together
/// with the transaction (or not at all); the Worker delivers them.
/// </summary>
public static class WalletNotifications
{
    public const string TransactionKind = "wallet-transaction";

    public static async Task QueueTransactionAsync(
        ExpensesDbContext db,
        Transaction transaction,
        string? categoryName,
        DateTime nowUtc,
        CancellationToken ct)
    {
        if (transaction.WalletId is not { } walletId)
            return;

        var wallet = await db.Wallets
            .AsNoTracking()
            .Where(w => w.Id == walletId)
            .Select(w => new
            {
                w.Name,
                Recipients = w.Members.Where(m => m.UserId != transaction.UserId).Select(m => m.UserId).ToList()
            })
            .FirstOrDefaultAsync(ct);

        if (wallet is null || wallet.Recipients.Count == 0)
            return;

        var author = await db.Users
            .Where(u => u.Id == transaction.UserId)
            .Select(u => u.DisplayName)
            .FirstOrDefaultAsync(ct);

        var text = Text(author ?? "Участник", wallet.Name, transaction, categoryName);

        db.OutboxMessages.AddRange(wallet.Recipients.Select(userId =>
            OutboxMessage.Create(userId, TransactionKind, text, nowUtc)));
    }

    public static string Text(string author, string walletName, Transaction transaction, string? categoryName)
    {
        var sign = transaction.Type == TransactionType.Income ? "➕" : "➖";
        var details = string.Join(" · ", new[] { categoryName, transaction.Note }.Where(x => !string.IsNullOrWhiteSpace(x)));

        return $"👥 {author} — кошелёк «{walletName}»\n" +
               $"{sign} {Currencies.FormatMoney(transaction.AmountMinor, transaction.Currency)}" +
               (details.Length > 0 ? $" — {details}" : "");
    }
}
