using ExpensesBot.Api.Common;
using ExpensesBot.Api.Contracts.Exchange;
using ExpensesBot.Api.Services;
using ExpensesBot.Data;

namespace ExpensesBot.Api.Endpoints;

public static class ExchangeEndpoints
{
    public static IEndpointRouteBuilder MapExchangeEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/exchange")
            .WithTags("Exchange");

        group.MapGet("/rates", GetRatesAsync);
        group.MapPost("/convert", ConvertAsync);

        return app;
    }

    private static async Task<IResult> GetRatesAsync(
        ExchangeRateProvider provider,
        CancellationToken ct)
    {
        var table = await provider.GetAsync(ct);

        var response = table.Rates
            .OrderBy(x => x.Key.From)
            .ThenBy(x => x.Key.To)
            .Select(x => new ExchangeRateResponse(
                x.Key.From,
                x.Key.To,
                x.Value,
                table.AsOfUtc,
                table.IsFallback))
            .ToList();

        return Results.Ok(response);
    }

    private static async Task<IResult> ConvertAsync(
        ConvertRequest request,
        ExchangeRateProvider provider,
        CancellationToken ct)
    {
        if (request.AmountMinor <= 0)
            return Error.Validation("Amount must be positive.").ToProblem();

        if (!Currencies.TryNormalize(request.FromCurrency, out var from) ||
            !Currencies.TryNormalize(request.ToCurrency, out var to))
        {
            return Error.Validation($"Supported currencies: {Currencies.SupportedList}.").ToProblem();
        }

        var table = await provider.GetAsync(ct);
        var rate = table.GetRate(from, to);

        if (rate is null)
            return Error.Validation($"No rate available for {from} -> {to}.").ToProblem();

        var converted = table.Convert(request.AmountMinor, from, to)!.Value;

        return Results.Ok(new ConvertResponse(
            request.AmountMinor,
            from,
            converted,
            to,
            rate.Value));
    }
}
