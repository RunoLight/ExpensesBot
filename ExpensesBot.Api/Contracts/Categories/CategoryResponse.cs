namespace ExpensesBot.Api.Contracts.Categories;

public sealed record CategoryResponse(
    long Id,
    string Name,
    string? Icon,
    bool IsArchived);

public sealed record CreateCategoryRequest(
    string Name,
    string? Icon);

public sealed record UpdateCategoryRequest(
    string Name,
    string? Icon);