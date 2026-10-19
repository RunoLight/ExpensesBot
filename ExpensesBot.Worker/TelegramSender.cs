using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ExpensesBot.Worker;

public enum DeliveryStatus
{
    Delivered,

    /// <summary>Telegram refused for good (bot blocked, chat not found): don't retry.</summary>
    Rejected,

    /// <summary>Network error, 5xx or 429: try again later.</summary>
    RetryLater
}

/// <param name="RetryAfter">Telegram's <c>retry_after</c> for 429 answers, when given.</param>
public sealed record DeliveryResult(DeliveryStatus Status, string? Error = null, TimeSpan? RetryAfter = null);

/// <summary>Sends a plain-text message and classifies the outcome for the outbox.</summary>
public sealed class TelegramSender(
    ILogger<TelegramSender> logger,
    IHttpClientFactory httpClientFactory)
{
    public async Task<DeliveryResult> SendTextAsync(string botToken, long chatId, string text, CancellationToken ct)
    {
        try
        {
            var http = httpClientFactory.CreateClient();
            var url = $"https://api.telegram.org/bot{botToken}/sendMessage";
            var payload = JsonConvert.SerializeObject(new { chat_id = chatId, text });

            using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(url, content, ct);

            if (response.IsSuccessStatusCode)
                return new DeliveryResult(DeliveryStatus.Delivered);

            var status = (int)response.StatusCode;
            var body = await response.Content.ReadAsStringAsync(ct);

            return new DeliveryResult(
                IsPermanentFailure(status) ? DeliveryStatus.Rejected : DeliveryStatus.RetryLater,
                $"{status} {body}",
                ReadRetryAfter(body));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Telegram sendMessage to {ChatId} failed", chatId);
            return new DeliveryResult(DeliveryStatus.RetryLater, ex.Message);
        }
    }

    /// <summary>
    /// 4xx answers (bot blocked, chat not found) will not change on retry;
    /// 429 (rate limit) and 5xx will.
    /// </summary>
    public static bool IsPermanentFailure(int statusCode) =>
        statusCode is >= 400 and < 500 and not 429;

    /// <summary>Reads <c>parameters.retry_after</c> (seconds) from a Telegram error body.</summary>
    public static TimeSpan? ReadRetryAfter(string body)
    {
        try
        {
            return JObject.Parse(body).SelectToken("parameters.retry_after")?.Value<int?>() is { } seconds
                ? TimeSpan.FromSeconds(seconds)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
