namespace ExpensesBot.Data.Entities;

public enum WalletRole
{
    Owner = 1,
    Member = 2
}

/// <summary>
/// A shared wallet (family budget): transactions recorded into it are visible
/// to every member. Each transaction still belongs to the member who recorded
/// it, so only the author can edit or delete it, and it stays in the author's
/// personal history and statistics too.
/// </summary>
public sealed class Wallet
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Code that lets someone join (also used in t.me/bot?start=join_CODE links).</summary>
    public string InviteCode { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public List<WalletMember> Members { get; set; } = [];
}

public sealed class WalletMember
{
    public long WalletId { get; set; }

    public Wallet Wallet { get; set; } = null!;

    public long UserId { get; set; }

    public User User { get; set; } = null!;

    public WalletRole Role { get; set; } = WalletRole.Member;

    public DateTime JoinedAtUtc { get; set; }
}
