using ExpensesBot.Client;
using ExpensesBot.Client.Localization;
using ExpensesBot.Client.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = builder.Configuration["ApiBaseUrl"] is { Length: > 0 } configured
    ? configured
    : "http://localhost:5069/";

builder.Services.AddScoped<TelegramService>();
builder.Services.AddScoped<LanguageState>();
builder.Services.AddTransient<TelegramAuthHandler>();

builder.Services
    .AddHttpClient<ExpensesApiClient>(client => client.BaseAddress = new Uri(apiBaseUrl))
    .AddHttpMessageHandler<TelegramAuthHandler>();

await builder.Build().RunAsync();
