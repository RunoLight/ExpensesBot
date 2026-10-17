using ExpensesBot.Api.Common;
using ExpensesBot.Data;

namespace ExpensesBot.Api.Application;

/// <summary>Validation rules shared by all money-related operations.</summary>
public static class Rules
{
    public const long MaxAmountMinor = 100_000_000_000L;
    public const int MaxNoteLength = 500;
    public const int MaxCategoryNameLength = 80;
    public const int MaxTagNameLength = 50;

    public static Error? ValidateAmount(long amountMinor, string field = "Amount") =>
        amountMinor <= 0 || amountMinor > MaxAmountMinor
            ? Error.Validation($"{field} must be positive and within the allowed limit.")
            : null;

    public static Error? ValidateCurrency(string? input, out string currency) =>
        Currencies.TryNormalize(input, out currency)
            ? null
            : Error.Validation($"Supported currencies: {Currencies.SupportedList}.");

    /// <summary>Trims the note; empty notes become null.</summary>
    public static Error? ValidateNote(string? input, out string? note)
    {
        note = string.IsNullOrWhiteSpace(input) ? null : input.Trim();
        return note?.Length > MaxNoteLength
            ? Error.Validation($"Note cannot exceed {MaxNoteLength} characters.")
            : null;
    }

    /// <summary>Treats unspecified DateTime values as UTC and converts local ones.</summary>
    public static DateTime? ToUtc(DateTime? value) =>
        value?.Kind switch
        {
            null => null,
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value!.Value, DateTimeKind.Utc)
        };
}
