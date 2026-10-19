using ExpensesBot.Api.Common;
using ExpensesBot.Api.Contracts.Profile;
using ExpensesBot.Data;
using ExpensesBot.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpensesBot.Api.Application;

public sealed class ProfileService(ExpensesDbContext db)
{
    public async Task<Result<UserProfileResponse>> GetAsync(long userId, CancellationToken ct)
    {
        var profile = await db.Users
            .AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => new UserProfileResponse(
                x.Id,
                x.DisplayName,
                x.DefaultCurrency,
                x.Transactions.Count,
                x.Categories.Count(c => !c.IsArchived)))
            .FirstOrDefaultAsync(ct);

        return profile is null ? Error.NotFound() : profile;
    }

    public async Task<Result<string>> SetDefaultCurrencyAsync(long userId, SetDefaultCurrencyRequest request, CancellationToken ct)
    {
        if (Rules.ValidateCurrency(request.Currency, out var currency) is { } error)
            return error;

        var updated = await db.Users
            .Where(x => x.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.DefaultCurrency, currency), ct);

        return updated == 0 ? Error.NotFound() : currency;
    }

    /// <summary>
    /// Deletes the user. Categories, transactions, budgets, goals, tags,
    /// recurring rules and wallet memberships go with it through ON DELETE
    /// CASCADE foreign keys. Wallets the user owns are deleted too (nobody
    /// could manage them otherwise); other members keep their transactions.
    /// </summary>
    public async Task<Result> DeleteAccountAsync(long userId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await db.Wallets
            .Where(w => w.Members.Any(m => m.UserId == userId && m.Role == WalletRole.Owner))
            .ExecuteDeleteAsync(ct);

        var deleted = await db.Users
            .Where(x => x.Id == userId)
            .ExecuteDeleteAsync(ct);

        await transaction.CommitAsync(ct);

        return deleted == 0 ? Error.NotFound() : Result.Success();
    }
}
