namespace ExpensesBot.Api.Contracts.Profile;

public sealed record SetDefaultCurrencyRequest(string Currency);

public sealed record SetLanguageRequest(string Language);

public sealed record UserProfileResponse(
    long Id,
    string? DisplayName,
    string DefaultCurrency,
    long TotalTransactions,
    long TotalCategories,
    string Language);
