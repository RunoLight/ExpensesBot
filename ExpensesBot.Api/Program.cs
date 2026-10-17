using ExpensesBot.Api.Application;
using ExpensesBot.Api.Authentication;
using ExpensesBot.Api.Endpoints;
using ExpensesBot.Api.Services;
using ExpensesBot.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddDbContext<ExpensesDbContext>(options =>
    options.UseSqlite(DatabaseConnection.Resolve(
        builder.Configuration.GetConnectionString("ExpensesDatabase"))));

// Authentication: Telegram Mini App initData (plus X-Dev-User-Id in Development).
builder.Services.AddSingleton(_ => new TelegramInitDataValidator(
    TimeSpan.FromHours(builder.Configuration.GetValue("Telegram:InitDataMaxAgeHours", 24))));
builder.Services.AddScoped<UserProvisioning>();
builder.Services
    .AddAuthentication(TelegramAuthenticationDefaults.Scheme)
    .AddScheme<AuthenticationSchemeOptions, TelegramAuthenticationHandler>(TelegramAuthenticationDefaults.Scheme, _ => { });
builder.Services.AddAuthorization();

// Application services: one per feature, endpoints only translate HTTP <-> service calls.
builder.Services.AddScoped<ExchangeRateProvider>();
builder.Services.AddScoped<TransactionService>();
builder.Services.AddScoped<SummaryService>();
builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<BudgetService>();
builder.Services.AddScoped<GoalService>();
builder.Services.AddScoped<RecurringTransactionService>();
builder.Services.AddScoped<TagService>();
builder.Services.AddScoped<ProfileService>();

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy("Client", policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

// Apply pending EF Core migrations on startup so a fresh clone (or container)
// works without running `dotnet ef database update` by hand.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider
        .GetRequiredService<ExpensesDbContext>()
        .Database.Migrate();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors("Client");
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapTransactionsEndpoints();
app.MapCategoriesEndpoints();
app.MapRecurringTransactionsEndpoints();
app.MapBudgetsEndpoints();
app.MapGoalsEndpoints();
app.MapExchangeEndpoints();
app.MapProfileEndpoints();
app.MapTagEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

// Exposed for WebApplicationFactory<Program> in integration tests.
public partial class Program { }
