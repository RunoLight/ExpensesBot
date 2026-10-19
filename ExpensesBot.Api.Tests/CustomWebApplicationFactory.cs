using System.Net;
using ExpensesBot.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ExpensesBot.Api.Tests;

/// <summary>
/// Runs the real API against a private in-memory SQLite database. Migrations are
/// applied by Program.cs on startup, so the tests also verify that the schema
/// can be created from the migrations.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string BotToken = "123456:TEST-bot-token-for-integration-tests";

    private readonly string _connectionString =
        $"Data Source=expensesbot-tests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

    // A shared-cache in-memory database lives as long as one connection is open.
    private readonly SqliteConnection _keepAlive;

    public CustomWebApplicationFactory()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
    }

    protected virtual string EnvironmentName => "Development";

    /// <summary>Extra configuration for derived factories.</summary>
    protected virtual IDictionary<string, string?> Settings => new Dictionary<string, string?>();

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ExpensesDatabase"] = _connectionString,
                ["Telegram:BotToken"] = BotToken
            });
            config.AddInMemoryCollection(Settings);
        });

        // The /health Telegram check must not reach the real Bot API from tests.
        builder.ConfigureServices(services => services
            .AddHttpClient(TelegramHealthCheck.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(TelegramGetMe)));

        return base.CreateHost(builder);
    }

    /// <summary>What the stubbed Telegram answers to getMe.</summary>
    protected virtual HttpResponseMessage TelegramGetMe() =>
        new(HttpStatusCode.OK) { Content = new StringContent("""{"ok":true,"result":{"id":1,"username":"expenses_test_bot"}}""") };

    private sealed class StubHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond());
    }

    public HttpClient CreateClientForDevUser(long telegramUserId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User-Id", telegramUserId.ToString());
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            _keepAlive.Dispose();
    }
}

/// <summary>Same API, started as Production: the development header must be ignored.</summary>
public class ProductionWebApplicationFactory : CustomWebApplicationFactory
{
    protected override string EnvironmentName => "Production";
}
