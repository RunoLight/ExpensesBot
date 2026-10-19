using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ExpensesBot.Api.Contracts.Profile;
using ExpensesBot.Api.Contracts.Exchange;
using ExpensesBot.Api.Contracts.Summary;
using ExpensesBot.Api.Contracts.Transactions;
using ExpensesBot.Data;
using ExchangeRate = ExpensesBot.Data.Entities.ExchangeRate;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace ExpensesBot.Api.Tests.Endpoints;

public class TransactionsEndpointsTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    private static object Transaction(
        long amountMinor,
        string currency = "AMD",
        int type = 0,
        string? note = null,
        long? categoryId = null) =>
        new { AmountMinor = amountMinor, Currency = currency, CategoryId = categoryId, Note = note, Type = type };

    private static async Task<TransactionResponse> CreateAsync(HttpClient client, object request)
    {
        var response = await client.PostAsJsonAsync("/api/v1/transactions", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<TransactionResponse>())!;
    }

    [Fact]
    public async Task CreateTransaction_ValidRequest_ReturnsCreatedTransaction()
    {
        var client = factory.CreateClientForDevUser(100_001);

        var created = await CreateAsync(client, Transaction(12_550, "usd", note: "  Lunch  "));

        created.Id.Should().BePositive();
        created.AmountMinor.Should().Be(12_550);
        created.Currency.Should().Be("USD");
        created.Note.Should().Be("Lunch");
        created.Type.Should().Be(TransactionType.Expense);
    }

    [Theory]
    [InlineData(-100, "AMD")]
    [InlineData(0, "AMD")]
    [InlineData(1000, "EUR")]
    [InlineData(1000, "")]
    public async Task CreateTransaction_InvalidInput_ReturnsBadRequest(long amountMinor, string currency)
    {
        var client = factory.CreateClientForDevUser(100_002);

        var response = await client.PostAsJsonAsync("/api/v1/transactions", Transaction(amountMinor, currency));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateTransaction_ChangesAreSaved()
    {
        var client = factory.CreateClientForDevUser(100_003);
        var created = await CreateAsync(client, Transaction(1_000, note: "before"));

        var response = await client.PutAsJsonAsync($"/api/v1/transactions/{created.Id}", new
        {
            AmountMinor = 2_500,
            Currency = "RUB",
            CategoryId = (long?)null,
            Note = "after",
            Type = 1
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var reloaded = await client.GetFromJsonAsync<TransactionResponse>($"/api/v1/transactions/{created.Id}");
        reloaded!.AmountMinor.Should().Be(2_500);
        reloaded.Currency.Should().Be("RUB");
        reloaded.Note.Should().Be("after");
        reloaded.Type.Should().Be(TransactionType.Income);
    }

    [Fact]
    public async Task DeleteTransaction_RemovesIt()
    {
        var client = factory.CreateClientForDevUser(100_004);
        var created = await CreateAsync(client, Transaction(3_000));

        var delete = await client.DeleteAsync($"/api/v1/transactions/{created.Id}");
        var get = await client.GetAsync($"/api/v1/transactions/{created.Id}");

        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Transactions_AreIsolatedBetweenUsers()
    {
        var owner = factory.CreateClientForDevUser(100_005);
        var stranger = factory.CreateClientForDevUser(100_006);
        var created = await CreateAsync(owner, Transaction(5_000));

        (await stranger.GetAsync($"/api/v1/transactions/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.DeleteAsync($"/api/v1/transactions/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/api/v1/transactions/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BulkCreate_ReportsInvalidRowsByIndex()
    {
        var client = factory.CreateClientForDevUser(100_007);

        var response = await client.PostAsJsonAsync("/api/v1/transactions/bulk", new[]
        {
            Transaction(1_000),
            Transaction(2_000, "EUR"),
            Transaction(3_000, "RUB", type: 1)
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<BulkCreateResponse>();

        result!.TotalCreated.Should().Be(2);
        result.Errors.Should().ContainSingle();
        result.Errors[0].Index.Should().Be(1);
        result.Errors[0].Error.Should().Contain("Supported currencies");
    }

    [Fact]
    public async Task BulkCreate_ExceedsLimit_ReturnsBadRequest()
    {
        var client = factory.CreateClientForDevUser(100_008);
        var requests = Enumerable.Range(0, 101).Select(_ => Transaction(1_000)).ToList();

        var response = await client.PostAsJsonAsync("/api/v1/transactions/bulk", requests);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Summary_SeparatesIncomeAndExpenses()
    {
        var client = factory.CreateClientForDevUser(100_009);
        await CreateAsync(client, Transaction(100_000));           // 1 000 AMD expense
        await CreateAsync(client, Transaction(50_000));            //   500 AMD expense
        await CreateAsync(client, Transaction(400_000, type: 1));  // 4 000 AMD income

        var summary = await client.GetFromJsonAsync<UserSummaryResponse>(
            "/api/v1/transactions/summary?mode=all&currency=AMD");

        summary!.Currency.Should().Be("AMD");
        summary.TotalExpenseMinor.Should().Be(150_000);
        summary.TotalIncomeMinor.Should().Be(400_000);
        summary.BalanceMinor.Should().Be(250_000);
        summary.TotalsByCurrency.Should().ContainKey("AMD").WhoseValue.Should().Be(150_000);
        summary.ByCategory.Sum(x => x.TotalAmountMinor).Should().Be(150_000, "income is not spending");
    }

    [Fact]
    public async Task Summary_ConvertsCurrenciesWithStoredRates()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();
            db.ExchangeRates.AddRange(
                new ExchangeRate { FromCurrency = "USD", ToCurrency = "AMD", Rate = 400m, FetchedAtUtc = DateTime.UtcNow },
                new ExchangeRate { FromCurrency = "AMD", ToCurrency = "USD", Rate = 0.0025m, FetchedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClientForDevUser(100_010);
        await CreateAsync(client, Transaction(1_000, "USD"));   // 10 USD = 4 000 AMD
        await CreateAsync(client, Transaction(100_000, "AMD")); // 1 000 AMD

        var inAmd = await client.GetFromJsonAsync<UserSummaryResponse>(
            "/api/v1/transactions/summary?mode=all&currency=AMD");
        var inUsd = await client.GetFromJsonAsync<UserSummaryResponse>(
            "/api/v1/transactions/summary?mode=all&currency=USD");

        inAmd!.TotalExpenseMinor.Should().Be(500_000);
        inUsd!.TotalExpenseMinor.Should().Be(1_250);
    }

    [Fact]
    public async Task Summary_AveragePerDayIsMoneyNotTransactionCount()
    {
        var client = factory.CreateClientForDevUser(100_011);
        await CreateAsync(client, Transaction(300_000)); // 3 000 AMD today

        var summary = await client.GetFromJsonAsync<UserSummaryResponse>(
            "/api/v1/transactions/summary?mode=30days&currency=AMD");

        // 3 000 AMD over 30 days = 100 AMD/day = 10 000 minor units.
        summary!.AveragePerDay.Should().BeApproximately(10_000, 1);
        summary.ProjectedMonthly.Should().BeApproximately(300_000, 30);
    }

    [Fact]
    public async Task ExportCsv_UsesImportCompatibleInvariantFormat()
    {
        var client = factory.CreateClientForDevUser(100_012);
        await CreateAsync(client, Transaction(12_345, note: "=HYPERLINK(\"x\")"));

        var bytes = await client.GetByteArrayAsync("/api/v1/transactions/export");
        var lines = Encoding.UTF8.GetString(bytes).TrimStart('﻿')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines[0].Should().StartWith("Дата (UTC),Тип,Категория,Сумма,Валюта,Комментарий");
        lines[1].Should().Contain(",123.45,AMD,");
        lines[1].Should().Contain("\"'=HYPERLINK(\"\"x\"\")\"", "formulas must be neutralised");
    }

    [Fact]
    public async Task ExchangeRates_ReturnFallbackWhenNothingStored()
    {
        // Uses its own factory so rates seeded by other tests don't interfere.
        await using var isolated = new CustomWebApplicationFactory();
        var client = isolated.CreateClient();

        var rates = await client.GetFromJsonAsync<List<ExchangeRateResponse>>("/api/v1/exchange/rates");

        rates.Should().NotBeEmpty();
        rates!.Should().OnlyContain(r => r.IsFallback);
        rates.Should().Contain(r => r.FromCurrency == "USD" && r.ToCurrency == "AMD" && r.Rate > 1);
    }

    [Fact]
    public async Task ValidationErrors_AreReturnedAsProblemDetails()
    {
        var client = factory.CreateClientForDevUser(100_013);

        var response = await client.PostAsJsonAsync("/api/v1/transactions", Transaction(1_000, "EUR"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("status").GetInt32().Should().Be(400);
        problem.RootElement.GetProperty("detail").GetString().Should().Contain("Supported currencies");
    }

    [Fact]
    public async Task DeleteAccount_RemovesTheUsersData()
    {
        var client = factory.CreateClientForDevUser(100_014);
        var profile = await client.GetFromJsonAsync<UserProfileResponse>("/api/v1/profile");
        await CreateAsync(client, Transaction(5_000));

        var response = await client.DeleteAsync("/api/v1/profile");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();
        (await db.Users.AnyAsync(u => u.Id == profile!.Id)).Should().BeFalse();
        (await db.Transactions.AnyAsync(t => t.UserId == profile!.Id)).Should().BeFalse();
        (await db.Categories.AnyAsync(c => c.UserId == profile!.Id)).Should().BeFalse();
    }

    private static Task<HttpResponseMessage> PostWithKeyAsync(HttpClient client, string key, object request)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transactions") { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(message);
    }

    [Fact]
    public async Task CreateTransaction_SameIdempotencyKey_ReturnsTheFirstTransaction()
    {
        var client = factory.CreateClientForDevUser(100_101);
        var key = Guid.NewGuid().ToString();

        var first = await PostWithKeyAsync(client, key, Transaction(1_500, note: "coffee"));
        var retry = await PostWithKeyAsync(client, key, Transaction(1_500, note: "coffee"));

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        retry.StatusCode.Should().Be(HttpStatusCode.Created);
        var original = await first.Content.ReadFromJsonAsync<TransactionResponse>();
        var replayed = await retry.Content.ReadFromJsonAsync<TransactionResponse>();
        replayed!.Id.Should().Be(original!.Id);

        var all = await client.GetFromJsonAsync<List<TransactionResponse>>("/api/v1/transactions");
        all.Should().ContainSingle();
    }

    [Fact]
    public async Task CreateTransaction_ConcurrentRequestsWithOneKey_CreateOneTransaction()
    {
        var client = factory.CreateClientForDevUser(100_102);
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => PostWithKeyAsync(client, key, Transaction(700))));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);
        var all = await client.GetFromJsonAsync<List<TransactionResponse>>("/api/v1/transactions");
        all.Should().ContainSingle();
    }

    [Fact]
    public async Task CreateTransaction_KeyReusedForAnotherAmount_ReturnsUnprocessable()
    {
        var client = factory.CreateClientForDevUser(100_103);
        var key = Guid.NewGuid().ToString();

        await PostWithKeyAsync(client, key, Transaction(1_000));
        var response = await PostWithKeyAsync(client, key, Transaction(2_000));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task CreateTransaction_SameKeyFromAnotherUser_IsIndependent()
    {
        var key = Guid.NewGuid().ToString();

        var first = await PostWithKeyAsync(factory.CreateClientForDevUser(100_104), key, Transaction(1_000));
        var second = await PostWithKeyAsync(factory.CreateClientForDevUser(100_105), key, Transaction(1_000));

        var a = await first.Content.ReadFromJsonAsync<TransactionResponse>();
        var b = await second.Content.ReadFromJsonAsync<TransactionResponse>();
        b!.Id.Should().NotBe(a!.Id);
    }

    [Fact]
    public async Task CreateTransaction_InvalidIdempotencyKey_ReturnsBadRequest()
    {
        var client = factory.CreateClientForDevUser(100_106);

        var response = await PostWithKeyAsync(client, new string('k', 65), Transaction(1_000));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
