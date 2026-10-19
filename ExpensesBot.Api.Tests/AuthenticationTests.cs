using System.Net;
using System.Net.Http.Json;
using ExpensesBot.Api.Contracts.Categories;
using ExpensesBot.Api.Contracts.Profile;
using ExpensesBot.Api.Tests.Infrastructure;
using FluentAssertions;

namespace ExpensesBot.Api.Tests.Endpoints;

/// <summary>Authentication as it works in production (no development header).</summary>
public class AuthenticationTests(ProductionWebApplicationFactory factory)
    : IClassFixture<ProductionWebApplicationFactory>
{
    private HttpClient ClientWithInitData(string initData)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"tma {initData}");
        return client;
    }

    [Fact]
    public async Task NoCredentials_ReturnsUnauthorized()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/profile");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DevelopmentHeader_IsIgnoredOutsideDevelopment()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User-Id", "42");

        var response = await client.GetAsync("/api/v1/profile");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ValidInitData_CreatesUserWithDefaultCategories()
    {
        var client = ClientWithInitData(
            TelegramInitData.Create(CustomWebApplicationFactory.BotToken, 700_001, "Анна"));

        var profile = await client.GetFromJsonAsync<UserProfileResponse>("/api/v1/profile");

        profile!.DisplayName.Should().Be("Анна");
        profile.DefaultCurrency.Should().Be("AMD");
        profile.TotalCategories.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(700_101, "ru", "ru", "Продукты")]
    [InlineData(700_102, "en", "en", "Groceries")]
    [InlineData(700_103, "de", "en", "Groceries")]
    public async Task NewUser_GetsTheLanguageOfTheirTelegramApp(long telegramUserId, string languageCode, string expected, string category)
    {
        var client = ClientWithInitData(TelegramInitData.Create(
            CustomWebApplicationFactory.BotToken, telegramUserId, languageCode: languageCode));

        var profile = await client.GetFromJsonAsync<UserProfileResponse>("/api/v1/profile");
        var categories = await client.GetFromJsonAsync<List<CategoryResponse>>("/api/v1/categories");

        profile!.Language.Should().Be(expected);
        categories!.Select(c => c.Name).Should().Contain(category);
    }

    [Fact]
    public async Task InitDataSignedWithAnotherToken_ReturnsUnauthorized()
    {
        var client = ClientWithInitData(
            TelegramInitData.Create("999:another-bot-token", 700_002));

        var response = await client.GetAsync("/api/v1/profile");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TamperedInitData_ReturnsUnauthorized()
    {
        var initData = TelegramInitData.Create(CustomWebApplicationFactory.BotToken, 700_003)
            .Replace("700003", "700004");

        var response = await ClientWithInitData(initData).GetAsync("/api/v1/profile");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExpiredInitData_ReturnsUnauthorized()
    {
        var initData = TelegramInitData.Create(
            CustomWebApplicationFactory.BotToken, 700_005, authDate: DateTimeOffset.UtcNow.AddDays(-3));

        var response = await ClientWithInitData(initData).GetAsync("/api/v1/profile");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
