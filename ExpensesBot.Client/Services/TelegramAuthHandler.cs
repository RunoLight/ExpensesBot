using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;

namespace ExpensesBot.Client.Services;

/// <summary>
/// Adds credentials to every API request.
/// Inside Telegram: "Authorization: tma &lt;initData&gt;", validated by the API.
/// Outside Telegram, Development builds only: "X-Dev-User-Id" so the UI can be
/// tried in a normal browser against a local API. Release builds never send it.
/// </summary>
public sealed class TelegramAuthHandler(
    IJSRuntime js,
    IWebAssemblyHostEnvironment environment) : DelegatingHandler
{
    private const string DevUserId = "123456789";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var initData = await js.InvokeAsync<string>("telegramApp.getInitData", cancellationToken);

        if (!string.IsNullOrWhiteSpace(initData))
        {
            // initData is a URL-encoded query string; add it verbatim.
            request.Headers.TryAddWithoutValidation("Authorization", $"tma {initData}");
        }
        else if (environment.IsDevelopment())
        {
            request.Headers.TryAddWithoutValidation("X-Dev-User-Id", DevUserId);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
