namespace ExpensesBot.Data.Entities;

public enum OutboxStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2
}

/// <summary>
/// A Telegram message waiting to be delivered (transactional outbox). The API and the
/// Worker add it in the same SaveChanges as the change it is about, so the message
/// exists if and only if that change was committed; the Worker's
/// <c>OutboxDispatcher</c> delivers it and retries transient failures.
/// </summary>
public sealed class OutboxMessage
{
    public const int MaxTextLength = 4096;
    public const int MaxKindLength = 32;
    public const int MaxErrorLength = 500;

    public long Id { get; set; }

    /// <summary>Recipient; the chat id is the user's Telegram id.</summary>
    public long UserId { get; set; }

    public User User { get; set; } = null!;

    /// <summary>What the message is, for logs and metrics ("daily-reminder", "wallet-transaction", …).</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Plain text, sent without parse_mode so user-entered names can't break it.</summary>
    public string Text { get; set; } = string.Empty;

    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;

    public int Attempts { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Not delivered before this moment; moved forward after a transient failure.</summary>
    public DateTime NextAttemptAtUtc { get; set; }

    public DateTime? SentAtUtc { get; set; }

    public string? LastError { get; set; }

    public static OutboxMessage Create(long userId, string kind, string text, DateTime nowUtc) => new()
    {
        UserId = userId,
        Kind = kind,
        Text = text.Length > MaxTextLength ? text[..MaxTextLength] : text,
        CreatedAtUtc = nowUtc,
        NextAttemptAtUtc = nowUtc
    };
}
