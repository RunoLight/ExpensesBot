using ExpensesBot.Data;
using ExpensesBot.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpensesBot.Api.Authentication;

/// <summary>Finds the local user for a Telegram account or creates it with default settings.</summary>
public sealed class UserProvisioning(ExpensesDbContext db)
{
    public async Task<User> FindOrCreateAsync(long telegramUserId, string? displayName, CancellationToken ct)
    {
        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, ct);

        if (user is not null)
            return user;

        user = UserFactory.Create(telegramUserId, displayName);
        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two first requests raced; the unique index on TelegramUserId
            // rejected the second insert, so load the row the first one created.
            db.ChangeTracker.Clear();
            return await db.Users
                .AsNoTracking()
                .FirstAsync(x => x.TelegramUserId == telegramUserId, ct);
        }

        db.ChangeTracker.Clear();
        return user;
    }
}
