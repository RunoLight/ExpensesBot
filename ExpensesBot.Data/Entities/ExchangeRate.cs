namespace ExpensesBot.Data.Entities;

public sealed class ExchangeRate
{
    public long Id { get; set; }

    public string FromCurrency { get; set; } = string.Empty;

    public string ToCurrency { get; set; } = string.Empty;

    public decimal Rate { get; set; }

    public DateTime FetchedAtUtc { get; set; } = DateTime.UtcNow;
}
