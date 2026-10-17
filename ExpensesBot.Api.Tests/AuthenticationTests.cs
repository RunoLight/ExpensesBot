using System.Net;
using System.Net.Http.Json;
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
        var response = await factory.CreateClient().GetAsync("/api/profile");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DevelopmentHeader_IsIgnoredOutsideDevelopment()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User-Id", "42");

        var response = await client.GetAsync("/api/profile");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ValidInitData_CreatesUserWithDefaultCategories()
    {
        var client = ClientWithInitData(
            TelegramInitData.Create(CustomWebApplicationFactory.BotToken, 700_001, "Анна"));

        var profile = await client.GetFromJsonAsync<UserProfileResponse>("/api/profile");

        profile!.DisplayName.Should().Be("Анна");
        profile.DefaultCurrency.Should().Be("AMD");
        profile.TotalCategories.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task InitDataSignedWithAnotherToken_ReturnsUnauthorized()
    {
        var client = ClientWithInitData(
            TelegramInitData.Create("999:another-bot-token", 700_002));

        var response = await client.GetAsync("/api/profile");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TamperedInitData_ReturnsUnauthorized()
    {
        var initData = TelegramInitData.Create(CustomWebApplicationFactory.BotToken, 700_003)
            .Replace("700003", "700004");

        var response = await ClientWithInitData(initData).GetAsync("/api/profile");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExpiredInitData_ReturnsUnauthorized()
    {
        var initData = TelegramInitData.Create(
            CustomWebApplicationFactory.BotToken, 700_005, authDate: DateTimeOffset.UtcNow.AddDays(-3));

        var response = await ClientWithInitData(initData).GetAsync("/api/profile");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
