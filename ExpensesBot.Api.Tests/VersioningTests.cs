using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace ExpensesBot.Api.Tests;

public class VersioningTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task RequestWithoutVersion_IsServedAsV1_AndReportsSupportedVersions()
    {
        var client = factory.CreateClientForDevUser(400_001);

        var response = await client.GetAsync("/api/categories");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("api-supported-versions").Should().ContainSingle().Which.Should().Be("1.0");
    }

    [Theory]
    [InlineData("header")]
    [InlineData("query")]
    public async Task ExplicitV1_IsAccepted(string where)
    {
        var client = factory.CreateClientForDevUser(400_002);
        var request = new HttpRequestMessage(HttpMethod.Get, where == "query" ? "/api/categories?api-version=1.0" : "/api/categories");
        if (where == "header")
            request.Headers.Add("api-version", "1.0");

        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnknownVersion_IsRejected()
    {
        var client = factory.CreateClientForDevUser(400_003);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/categories");
        request.Headers.Add("api-version", "2.0");

        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OpenApiDocument_DescribesV1AndAuthentication()
    {
        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.GetProperty("paths").TryGetProperty("/api/transactions", out _).Should().BeTrue();
        var schemes = root.GetProperty("components").GetProperty("securitySchemes");
        schemes.TryGetProperty("TelegramInitData", out _).Should().BeTrue();
        schemes.TryGetProperty("DevUser", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ScalarUi_IsServedInDevelopment()
    {
        var response = await factory.CreateClient().GetAsync("/scalar");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("ExpensesBot API");
    }
}

public class ProductionDocumentationTests(ProductionWebApplicationFactory factory) : IClassFixture<ProductionWebApplicationFactory>
{
    [Theory]
    [InlineData("/openapi/v1.json")]
    [InlineData("/scalar")]
    public async Task Documentation_IsNotPublishedInProduction(string path)
    {
        (await factory.CreateClient().GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
