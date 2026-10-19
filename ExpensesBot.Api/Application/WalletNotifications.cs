using ExpensesBot.Data;
using ExpensesBot.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpensesBot.Api.Application;

/// <summary>
/// Tells the other members of a shared wallet about a new transaction, each in their
/// own language. Messages are added to the outbox in the caller's unit of work, so
/// they are committed together with the transaction (or not at all); the Worker
/// delivers them.
/// </summary>
public static class WalletNotifications
{
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
                Recipients = w.Members
                    .Where(m => m.UserId != transaction.UserId)
                    .Select(m => new { m.UserId, m.User.Language })
                    .ToList()
            })
            .FirstOrDefaultAsync(ct);

        if (wallet is null || wallet.Recipients.Count == 0)
            return;

        var author = await db.Users
            .Where(u => u.Id == transaction.UserId)
            .Select(u => u.DisplayName)
            .FirstOrDefaultAsync(ct);

        foreach (var recipient in wallet.Recipients)
        {
            var text = MessageText.For(recipient.Language);
            var message = text.WalletTransaction(
                author ?? text.MemberFallbackName(transaction.UserId),
                wallet.Name,
                transaction.Type,
                transaction.AmountMinor,
                transaction.Currency,
                categoryName,
                transaction.Note);

            db.OutboxMessages.Add(OutboxMessage.Create(recipient.UserId, OutboxKind.WalletTransaction, message, nowUtc));
        }
    }
}
