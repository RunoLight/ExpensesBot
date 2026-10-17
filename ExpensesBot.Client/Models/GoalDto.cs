namespace ExpensesBot.Client.Models;

public sealed record GoalDto(
    long Id,
    string Name,
    long TargetAmountMinor,
    string Currency,
    long? CategoryId,
    string? CategoryName,
    string? CategoryIcon,
    DateTime? TargetDateUtc,
    long SavedAmountMinor,
    double ProgressPercent,
    bool IsActive,
    DateTime CreatedAtUtc);

public sealed record GoalContribution(long GoalId, long AmountMinor);
