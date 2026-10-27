using System.Security.Claims;
using ExpensesBot.Api.Common;

namespace ExpensesBot.Api.Authentication;

public static class ClaimsPrincipalExtensions
{
    /// <summary>Local (database) id of the authenticated user.</summary>
    public static long GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return long.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("The request is not authenticated.");
    }

    /// <summary>
    /// Error texts in the signed-in user's language; for anonymous requests the
    /// first supported language in Accept-Language, English otherwise.
    /// </summary>
    public static ErrorText GetErrorText(this HttpContext context)
    {
        if (context.User.FindFirstValue(TelegramAuthenticationDefaults.LanguageClaim) is { } language)
            return ErrorText.For(language);

        var accepted = context.Request.GetTypedHeaders().AcceptLanguage
            .OrderByDescending(x => x.Quality ?? 1)
            .Select(x => x.Value.Value)
            .FirstOrDefault(x => x is not null && (x.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ||
                                                   x.StartsWith("en", StringComparison.OrdinalIgnoreCase)));

        return ErrorText.For(Data.Languages.FromTelegram(accepted));
    }
}
