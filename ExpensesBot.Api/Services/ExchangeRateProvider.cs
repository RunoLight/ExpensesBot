using ExpensesBot.Data;
using Microsoft.EntityFrameworkCore;

namespace ExpensesBot.Api.Services;

/// <summary>
/// Loads the latest exchange rates written by the Worker (Central Bank of
/// Russia feed). If the Worker has not stored fresh rates yet, falls back to
/// approximate built-in rates so conversion keeps working; the table reports
/// that via <see cref="ExchangeRateTable.IsFallback"/>.
/// </summary>
public sealed class ExchangeRateProvider(ExpensesDbContext db)
{
    // CBR does not publish on weekends/holidays, so allow a few days.
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(4);

    // Approximate value of one unit in AMD; used only when no fresh data exists.
    private static readonly Dictionary<string, decimal> FallbackAmdPerUnit = new()
    {
        ["AMD"] = 1m,
        ["USD"] = 390m,
        ["RUB"] = 4.8m
    };

    public async Task<ExchangeRateTable> GetAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - MaxAge;

        var rows = await db.ExchangeRates
            .AsNoTracking()
            .Where(x => x.FetchedAtUtc >= cutoff)
            .ToListAsync(ct);

        var latest = rows
            .GroupBy(x => (x.FromCurrency, x.ToCurrency))
            .Select(g => g.OrderByDescending(x => x.FetchedAtUtc).First())
            .ToList();

        var fallback = CreateFallback();
        if (latest.Count == 0)
            return fallback;

        // Stored rates win; any pair the Worker didn't store keeps its fallback value.
        var rates = new Dictionary<(string From, string To), decimal>(fallback.Rates);
        foreach (var rate in latest)
            rates[(rate.FromCurrency, rate.ToCurrency)] = rate.Rate;

        var hasAllPairs = fallback.Rates.Keys.All(pair =>
            latest.Any(x => x.FromCurrency == pair.From && x.ToCurrency == pair.To));

        return new ExchangeRateTable(
            rates,
            latest.Max(x => x.FetchedAtUtc),
            IsFallback: !hasAllPairs);
    }

    private static ExchangeRateTable CreateFallback()
    {
        var rates = new Dictionary<(string From, string To), decimal>();

        foreach (var from in Currencies.Supported)
        foreach (var to in Currencies.Supported)
        {
            if (from != to)
                rates[(from, to)] = FallbackAmdPerUnit[from] / FallbackAmdPerUnit[to];
        }

        return new ExchangeRateTable(rates, DateTime.UtcNow, IsFallback: true);
    }
}

public sealed record ExchangeRateTable(
    IReadOnlyDictionary<(string From, string To), decimal> Rates,
    DateTime AsOfUtc,
    bool IsFallback)
{
    public decimal? GetRate(string from, string to)
    {
        if (from == to)
            return 1m;

        if (Rates.TryGetValue((from, to), out var direct))
            return direct;

        if (Rates.TryGetValue((to, from), out var inverse) && inverse != 0)
            return 1m / inverse;

        // Cross rate through AMD.
        if (from != Currencies.Default && to != Currencies.Default &&
            GetRate(from, Currencies.Default) is { } toBase &&
            GetRate(Currencies.Default, to) is { } fromBase)
        {
            return toBase * fromBase;
        }

        return null;
    }

    /// <summary>Converts minor units; returns null when no rate is known.</summary>
    public long? Convert(long amountMinor, string from, string to)
    {
        var rate = GetRate(from, to);
        if (rate is null)
            return null;

        return (long)decimal.Round(amountMinor * rate.Value, 0, MidpointRounding.AwayFromZero);
    }
}
