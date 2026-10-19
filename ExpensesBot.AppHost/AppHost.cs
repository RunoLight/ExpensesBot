// Local orchestration with the .NET Aspire dashboard: `dotnet run --project ExpensesBot.AppHost`
// starts the API, the Worker and the Mini App, and collects their logs, traces and metrics.
// The bot token still comes from the shared user-secrets store (see the README).

var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.ExpensesBot_Api>("api", launchProfileName: "http")
    .WithHttpHealthCheck("/health");

// The API applies migrations on startup; the Worker waits so they don't race for the lock.
builder.AddProject<Projects.ExpensesBot_Worker>("worker")
    .WaitFor(api);

// The Blazor WebAssembly client reads the API URL from wwwroot/appsettings.Development.json.
builder.AddProject<Projects.ExpensesBot_Client>("client", launchProfileName: "http")
    .WaitFor(api);

builder.Build().Run();
