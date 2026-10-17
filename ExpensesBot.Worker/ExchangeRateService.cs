using ExpensesBot.Data;
using ExpensesBot.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace ExpensesBot.Worker;

/// <summary>
/// Downloads daily rates from the Central Bank of Russia (cbr-xml-daily.ru)
/// and stores a rate for every pair of supported currencies.
///
/// The feed is RUB-based: for each currency it gives how many roubles
/// <c>Nominal</c> units cost (e.g. AMD: Nominal 100, Value 24.3 means
/// 100 AMD = 24.3 RUB). Every cross rate is derived from those values:
/// rate(from → to) = rubPerUnit[from] / rubPerUnit[to].
/// </summary>
public sealed class ExchangeRateService(
    ILogger<ExchangeRateService> logger,
    IHttpClientFactory httpClientFactory,
    IServiceProvider serviceProvider)
{
    private const string CbrUrl = "https://www.cbr-xml-daily.ru/daily_json.js";

    // CBR publishes once per business day; refreshing more often is pointless.
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(6);

    public async Task UpdateRatesAsync(CancellationToken ct)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();

            var lastFetched = await db.ExchangeRates
                .OrderByDescending(x => x.FetchedAtUtc)
                .Select(x => (DateTime?)x.FetchedAtUtc)
                .FirstOrDefaultAsync(ct);

            if (lastFetched is not null && DateTime.UtcNow - lastFetched.Value < RefreshInterval)
                return;

            var http = httpClientFactory.CreateClient();
            var json = JObject.Parse(await http.GetStringAsync(CbrUrl, ct));

            var rubPerUnit = ParseRubPerUnit(json);
            if (rubPerUnit is null)
            {
                logger.LogWarning("CBR response did not contain all supported currencies");
                return;
            }

            await SaveRatesAsync(db, rubPerUnit, ct);

            logger.LogInformation(
                "Exchange rates updated: 1 USD = {UsdAmd:F2} AMD, 1 RUB = {RubAmd:F3} AMD",
                rubPerUnit["USD"] / rubPerUnit["AMD"],
                rubPerUnit["RUB"] / rubPerUnit["AMD"]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to update exchange rates");
        }
    }

    /// <summary>Returns RUB price of one unit of each supported currency.</summary>
    public static Dictionary<string, decimal>? ParseRubPerUnit(JObject json)
    {
        var result = new Dictionary<string, decimal> { ["RUB"] = 1m };

        foreach (var code in Currencies.Supported.Where(c => c != "RUB"))
        {
            var valute = json["Valute"]?[code];
            var value = valute?["Value"]?.Value<decimal?>();
            var nominal = valute?["Nominal"]?.Value<decimal?>() ?? 1m;

            if (value is null or <= 0 || nominal <= 0)
                return null;

            result[code] = value.Value / nominal;
        }

        return result;
    }

    private static async Task SaveRatesAsync(
        ExpensesDbContext db,
        Dictionary<string, decimal> rubPerUnit,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var rates = new List<ExchangeRate>();

        foreach (var from in Currencies.Supported)
        foreach (var to in Currencies.Supported)
        {
            if (from == to)
                continue;

            rates.Add(new ExchangeRate
            {
                FromCurrency = from,
                ToCurrency = to,
                Rate = decimal.Round(rubPerUnit[from] / rubPerUnit[to], 8),
                FetchedAtUtc = now
            });
        }

        // Replace the previous snapshot atomically so readers never see an empty table.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.ExchangeRates.ExecuteDeleteAsync(ct);
        db.ExchangeRates.AddRange(rates);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
