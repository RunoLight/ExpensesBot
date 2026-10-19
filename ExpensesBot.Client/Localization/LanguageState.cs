namespace ExpensesBot.Client.Localization;

/// <summary>
/// The current UI language. <c>App.razor</c> cascades <see cref="Text"/> to every
/// component and re-renders when <see cref="Changed"/> fires, so switching the
/// language takes effect at once, without reloading the app.
/// </summary>
public sealed class LanguageState
{
    public UiText Text { get; private set; } = UiText.En;

    public event Action? Changed;

    public void Set(string? language)
    {
        var text = UiText.For(language);
        if (ReferenceEquals(text, Text))
            return;

        Text = text;
        Changed?.Invoke();
    }
}
