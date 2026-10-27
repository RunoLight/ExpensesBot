using System.Security.Claims;
using System.Text.Encodings.Web;
using ExpensesBot.Data;
using ExpensesBot.ServiceDefaults;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ExpensesBot.Api.Authentication;

public static class TelegramAuthenticationDefaults
{
    public const string Scheme = "Telegram";

    /// <summary>Header sent by the Mini App: <c>Authorization: tma &lt;initData&gt;</c>.</summary>
    public const string AuthorizationPrefix = "tma ";

    /// <summary>Development-only shortcut so the API can be called without Telegram.</summary>
    public const string DevelopmentUserHeader = "X-Dev-User-Id";

    public const string TelegramIdClaim = "telegram_id";

    /// <summary>The user's profile language ("en"/"ru"): error messages are written in it.</summary>
    public const string LanguageClaim = "language";
}

/// <summary>
/// Authenticates requests from the Telegram Mini App by validating the signed
/// initData (HMAC-SHA256 with the bot token), then maps the Telegram user to a
/// local user, creating it on the first visit. The local user id is exposed as
/// <see cref="ClaimTypes.NameIdentifier"/>.
///
/// In the Development environment only, <c>X-Dev-User-Id</c> is accepted too.
/// </summary>
public sealed class TelegramAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    TelegramInitDataValidator validator,
    UserProvisioning provisioning,
    IConfiguration configuration,
    IWebHostEnvironment environment)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (environment.IsDevelopment() &&
            Request.Headers.TryGetValue(TelegramAuthenticationDefaults.DevelopmentUserHeader, out var devHeader) &&
            long.TryParse(devHeader.ToString(), out var devTelegramId) &&
            devTelegramId > 0)
        {
            return await SignInAsync(devTelegramId, $"Dev user {devTelegramId}", languageCode: null);
        }

        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith(TelegramAuthenticationDefaults.AuthorizationPrefix, StringComparison.Ordinal))
            return AuthenticateResult.NoResult();

        var botToken = TelegramBotToken.Get(configuration);
        if (string.IsNullOrWhiteSpace(botToken))
        {
            Logger.LogError("Telegram bot token is not configured; Mini App requests cannot be authenticated.");
            return AuthenticateResult.Fail("Authentication is not configured.");
        }

        var initData = authorization[TelegramAuthenticationDefaults.AuthorizationPrefix.Length..];

        if (!validator.TryValidate(initData, botToken, out var telegramUserId))
            return AuthenticateResult.Fail("Invalid or expired Telegram init data.");

        return await SignInAsync(telegramUserId,
            TelegramInitDataValidator.ReadDisplayName(initData),
            TelegramInitDataValidator.ReadLanguageCode(initData));
    }

    private async Task<AuthenticateResult> SignInAsync(long telegramUserId, string? displayName, string? languageCode)
    {
        var user = await provisioning.FindOrCreateAsync(telegramUserId, displayName, languageCode, Context.RequestAborted);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(TelegramAuthenticationDefaults.TelegramIdClaim, telegramUserId.ToString()),
            new Claim(TelegramAuthenticationDefaults.LanguageClaim, user.Language)
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
