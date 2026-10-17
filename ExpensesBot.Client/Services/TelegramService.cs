using Microsoft.JSInterop;

namespace ExpensesBot.Client.Services;

/// <summary>Thin wrapper over the Telegram Mini App JS API (wwwroot/js/telegram.js).</summary>
public sealed class TelegramService(IJSRuntime js)
{
    /// <summary>Tells Telegram the app is ready and expands it to full height.</summary>
    public ValueTask ReadyAsync() =>
        js.InvokeVoidAsync("telegramApp.ready");

    /// <summary>Whether Telegram uses a dark colour scheme. False outside Telegram.</summary>
    public ValueTask<bool> IsDarkThemeAsync() =>
        js.InvokeAsync<bool>("telegramApp.isDarkTheme");

    /// <summary>
    /// Returns the "startapp" deep-link parameter. With a key, "key_value"
    /// is reduced to "value" and parameters for other keys return null.
    /// </summary>
    public ValueTask<string?> GetStartParamAsync(string? key = null) =>
        js.InvokeAsync<string?>("telegramApp.getStartParam", key ?? "");
}
