using System.Security.Claims;

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
}
