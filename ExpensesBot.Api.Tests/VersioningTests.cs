using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace ExpensesBot.Api.Tests;

public class VersioningTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Theory]
    [InlineData("/api/v1/categories")]
    [InlineData("/api/v1.0/categories")]
    public async Task V1InThePath_IsServed_AndReportsSupportedVersions(string path)
    {
        var client = factory.CreateClientForDevUser(400_001);

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("api-supported-versions").Should().ContainSingle().Which.Should().Be("1.0");
    }

    [Fact]
    public async Task UnknownVersion_IsRejected()
    {
        var client = factory.CreateClientForDevUser(400_002);

        var response = await client.GetAsync("/api/v2/categories");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UnversionedPath_IsNotAnEndpoint()
    {
        var client = factory.CreateClientForDevUser(400_003);

        (await client.GetAsync("/api/categories")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Created_PointsToTheVersionedResource()
    {
        var client = factory.CreateClientForDevUser(400_004);

        var response = await client.PostAsync("/api/v1/tags",
            JsonContent.Create(new { Name = "trip", Color = "#36A2EB" }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.OriginalString.Should().StartWith("/api/v1/tags/");
    }

    [Fact]
    public async Task OpenApiDocument_DescribesV1AndAuthentication()
    {
        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.GetProperty("paths").TryGetProperty("/api/v1/transactions", out _).Should().BeTrue();
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
