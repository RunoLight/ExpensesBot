using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace ExpensesBot.Api.Tests;

public class HealthTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task Health_ReportsDatabaseAndTelegram()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = await response.Content.ReadFromJsonAsync<JsonElement>();
        report.GetProperty("status").GetString().Should().Be("Healthy");

        var checks = report.GetProperty("checks");
        checks.GetProperty("database").GetProperty("status").GetString().Should().Be("Healthy");
        checks.GetProperty("telegram").GetProperty("status").GetString().Should().Be("Healthy");
        checks.GetProperty("telegram").GetProperty("description").GetString().Should().Be("@expenses_test_bot");
    }

    [Fact]
    public async Task Version_ReportsTheApisBuild_WithoutAuthentication()
    {
        var info = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/version");

        var expected = typeof(Program).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion;
        info.GetProperty("version").GetString().Should().Be(expected.Split('+')[0]);
    }

    [Fact]
    public async Task Alive_DoesNotRunDependencyChecks()
    {
        var response = await factory.CreateClient().GetAsync("/alive");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }
}

/// <summary>Telegram rejecting the token degrades the report but keeps the API serving.</summary>
public class TelegramDownWebApplicationFactory : CustomWebApplicationFactory
{
    protected override HttpResponseMessage TelegramGetMe() =>
        new(HttpStatusCode.Unauthorized) { Content = new StringContent("""{"ok":false,"error_code":401}""") };
}

public class DegradedHealthTests(TelegramDownWebApplicationFactory factory) : IClassFixture<TelegramDownWebApplicationFactory>
{
    [Fact]
    public async Task Health_TelegramUnavailable_IsDegradedNotDown()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = await response.Content.ReadFromJsonAsync<JsonElement>();
        report.GetProperty("status").GetString().Should().Be("Degraded");
        report.GetProperty("checks").GetProperty("telegram").GetProperty("description").GetString()
            .Should().Contain("401");
    }
}
