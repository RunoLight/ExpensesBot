namespace ExpensesBot.Api.Contracts.Exchange;

public sealed record ExchangeRateResponse(
    string FromCurrency,
    string ToCurrency,
    decimal Rate,
    DateTime UpdatedAtUtc,
    bool IsFallback);

public sealed record ConvertRequest(
    long AmountMinor,
    string FromCurrency,
    string ToCurrency);

public sealed record ConvertResponse(
    long OriginalAmountMinor,
    string OriginalCurrency,
    long ConvertedAmountMinor,
    string ConvertedCurrency,
    decimal Rate);
