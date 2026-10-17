using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
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
        });

        return base.CreateHost(builder);
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
