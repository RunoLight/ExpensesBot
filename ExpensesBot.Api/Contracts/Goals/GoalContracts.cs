namespace ExpensesBot.Api.Contracts.Goals;

public sealed record CreateGoalRequest(
    string? Name,
    long TargetAmountMinor,
    string Currency,
    long? CategoryId,
    DateTime? TargetDateUtc);

/// <summary>
/// The currency and category of a goal are fixed: contributions already
/// recorded in them would stop counting.
/// </summary>
public sealed record UpdateGoalRequest(
    string? Name,
    long TargetAmountMinor,
    DateTime? TargetDateUtc,
    bool IsActive = true);

public sealed record ContributeToGoalRequest(
    long AmountMinor);

public sealed record GoalResponse(
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

public sealed record ContributionResponse(
    long TransactionId,
    long AmountMinor);
