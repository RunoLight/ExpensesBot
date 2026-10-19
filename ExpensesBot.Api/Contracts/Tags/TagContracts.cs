namespace ExpensesBot.Api.Contracts.Tags;

public sealed record CreateTagRequest(
    string Name,
    string Color);

public sealed record UpdateTagRequest(
    string Name,
    string? Color);

public sealed record TagResponse(
    long Id,
    string Name,
    string Color,
    DateTime CreatedAtUtc);
