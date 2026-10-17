namespace ExpensesBot.Client.Models;

public sealed record UserProfileDto(
    long Id,
    string? DisplayName,
    string DefaultCurrency,
    long TotalTransactions,
    long TotalCategories);
