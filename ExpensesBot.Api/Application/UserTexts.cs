using ExpensesBot.Data;
using Microsoft.EntityFrameworkCore;

namespace ExpensesBot.Api.Application;

public static class UserTexts
{
    /// <summary>Texts in the user's language, for names the API puts into responses and files.</summary>
    public static async Task<MessageText> TextForAsync(this ExpensesDbContext db, long userId, CancellationToken ct) =>
        MessageText.For(await db.Users.Where(u => u.Id == userId).Select(u => u.Language).FirstOrDefaultAsync(ct));
}
