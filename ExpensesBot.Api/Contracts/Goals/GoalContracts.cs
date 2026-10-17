namespace ExpensesBot.Api.Contracts.Goals;

public sealed record CreateGoalRequest(
    string? Name,
    long TargetAmountMinor,
    string Currency,
    long? CategoryId,
    DateTime? TargetDateUtc);

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
