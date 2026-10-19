using System.Security.Cryptography;
using ExpensesBot.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpensesBot.Data;

public enum JoinWalletStatus
{
    Joined,
    AlreadyMember,
    NotFound,
    Full
}

public sealed record JoinWalletResult(JoinWalletStatus Status, Wallet? Wallet);

/// <summary>
/// Invite codes and joining a wallet. Shared by the API (Mini App) and the
/// Worker (bot deep link /start join_CODE), so both follow the same rules.
/// </summary>
public static class WalletInvites
{
    public const int CodeLength = 8;
    public const int MaxMembers = 20;
    public const string DeepLinkPrefix = "join_";

    // No 0/O or 1/I/L: codes are read aloud and typed by hand.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public static string NewCode() => RandomNumberGenerator.GetString(Alphabet, CodeLength);

    /// <summary>"abcd-2345 " → "ABCD2345"; null when it can't be a code.</summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var code = new string(input.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return code.Length == CodeLength && code.All(Alphabet.Contains) ? code : null;
    }

    public static async Task<JoinWalletResult> JoinAsync(
        ExpensesDbContext db,
        long userId,
        string? code,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var normalized = Normalize(code);
        if (normalized is null)
            return new JoinWalletResult(JoinWalletStatus.NotFound, null);

        var wallet = await db.Wallets
            .Include(w => w.Members)
            .FirstOrDefaultAsync(w => w.InviteCode == normalized, ct);

        if (wallet is null)
            return new JoinWalletResult(JoinWalletStatus.NotFound, null);

        if (wallet.Members.Any(m => m.UserId == userId))
            return new JoinWalletResult(JoinWalletStatus.AlreadyMember, wallet);

        if (wallet.Members.Count >= MaxMembers)
            return new JoinWalletResult(JoinWalletStatus.Full, wallet);

        wallet.Members.Add(new WalletMember
        {
            UserId = userId,
            Role = WalletRole.Member,
            JoinedAtUtc = nowUtc
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Joined twice at the same moment (double tap): the primary key kept one row.
            db.ChangeTracker.Clear();
            return new JoinWalletResult(JoinWalletStatus.AlreadyMember, wallet);
        }

        return new JoinWalletResult(JoinWalletStatus.Joined, wallet);
    }
}
