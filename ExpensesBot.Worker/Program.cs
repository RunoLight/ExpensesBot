using ExpensesBot.Data;
using ExpensesBot.Worker;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHttpClient();

builder.Services.AddDbContext<ExpensesDbContext>(options =>
    options.UseSqlite(DatabaseConnection.Resolve(
        builder.Configuration.GetConnectionString("ExpensesDatabase"))));

builder.Services.AddSingleton<NotificationService>();
builder.Services.AddSingleton<TelegramBotHandler>();
builder.Services.AddSingleton<ExchangeRateService>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

// The API applies migrations too; EF Core takes a migration lock, so whichever
// process starts first creates the schema and the other one is a no-op.
using (var scope = host.Services.CreateScope())
{
    scope.ServiceProvider
        .GetRequiredService<ExpensesDbContext>()
        .Database.Migrate();
}

host.Run();
