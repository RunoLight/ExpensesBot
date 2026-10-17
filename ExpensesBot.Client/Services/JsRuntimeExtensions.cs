using Microsoft.JSInterop;

namespace ExpensesBot.Client.Services;

public static class JsRuntimeExtensions
{
    /// <summary>
    /// Asks the user to confirm an action. Uses Telegram's native dialog inside
    /// the Mini App (window.confirm is not available in every Telegram client)
    /// and the browser dialog elsewhere.
    /// </summary>
    public static ValueTask<bool> ConfirmAsync(this IJSRuntime js, string message) =>
        js.InvokeAsync<bool>("telegramApp.confirm", message);
}
