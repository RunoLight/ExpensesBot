namespace ExpensesBot.Client.Models;

public sealed record ExchangeRateDto(
    string FromCurrency,
    string ToCurrency,
    decimal Rate,
    DateTime UpdatedAtUtc,
    bool IsFallback = false);
