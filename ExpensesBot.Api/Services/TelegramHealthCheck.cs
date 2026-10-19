using System.Text.Json;
using ExpensesBot.Api.Authentication;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ExpensesBot.Api.Services;

/// <summary>
/// Calls Telegram's <c>getMe</c> with the bot token. The API itself keeps working
/// without Telegram (initData is verified offline), so problems are reported as
/// Degraded, not Unhealthy. The result is cached briefly so frequent probes don't
/// turn into a stream of Telegram requests (registered as a singleton for that).
/// </summary>
public sealed class TelegramHealthCheck(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    TimeProvider timeProvider) : IHealthCheck
{
    public const string HttpClientName = "telegram-health";

    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(1);

    private readonly Lock _cacheLock = new();
    private (HealthCheckResult Result, DateTimeOffset At)? _cached;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        lock (_cacheLock)
        {
            if (_cached is { } cached && timeProvider.GetUtcNow() - cached.At < CacheFor)
                return cached.Result;
        }

        var result = await ProbeAsync(ct);

        lock (_cacheLock)
            _cached = (result, timeProvider.GetUtcNow());

        return result;
    }

    private async Task<HealthCheckResult> ProbeAsync(CancellationToken ct)
    {
        var botToken = TelegramBotToken.Get(configuration);
        if (string.IsNullOrWhiteSpace(botToken))
            return HealthCheckResult.Degraded("Bot token is not configured.");

        try
        {
            var http = httpClientFactory.CreateClient(HttpClientName);
            using var response = await http.GetAsync($"https://api.telegram.org/bot{botToken}/getMe", ct);

            if (!response.IsSuccessStatusCode)
                return HealthCheckResult.Degraded($"getMe returned {(int)response.StatusCode}.");

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var username = body.RootElement.GetProperty("result").GetProperty("username").GetString();
            return HealthCheckResult.Healthy($"@{username}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            return HealthCheckResult.Degraded("Telegram is unreachable.", ex);
        }
    }
}
