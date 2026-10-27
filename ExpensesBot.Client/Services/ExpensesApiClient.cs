using System.Net.Http.Json;
using ExpensesBot.Client.Models;

namespace ExpensesBot.Client.Services;

/// <summary>
/// Typed client for ExpensesBot.Api. Authentication headers are added by
/// <see cref="TelegramAuthHandler"/>, so every method is safe to call concurrently.
/// </summary>
public sealed class ExpensesApiClient(HttpClient http)
{
    // Categories

    public async Task<List<CategoryDto>> GetCategoriesAsync(CancellationToken ct = default) =>
        await GetAsync<List<CategoryDto>>("api/v1/categories", ct) ?? [];

    public Task<CategoryDto> CreateCategoryAsync(string name, string? icon = null, CancellationToken ct = default) =>
        SendAsync<CategoryDto>(HttpMethod.Post, "api/v1/categories", new { Name = name, Icon = icon }, ct);

    public Task<CategoryDto> UpdateCategoryAsync(long id, string name, string? icon, CancellationToken ct = default) =>
        SendAsync<CategoryDto>(HttpMethod.Put, $"api/v1/categories/{id}", new { Name = name, Icon = icon }, ct);

    public Task DeleteCategoryAsync(long id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/v1/categories/{id}", null, ct);

    public Task<CategoryDto> ArchiveCategoryAsync(long id, CancellationToken ct = default) =>
        SendAsync<CategoryDto>(HttpMethod.Post, $"api/v1/categories/{id}/archive", null, ct);

    // Transactions

    /// <summary>A page of the history; the API filters and sorts.</summary>
    public async Task<List<TransactionDto>> GetTransactionsAsync(HistoryFilter filter, int offset = 0,
        int limit = HistoryFilter.PageSize, CancellationToken ct = default) =>
        await GetAsync<List<TransactionDto>>($"api/v1/transactions?{filter.ToQueryString()}&limit={limit}&offset={offset}", ct) ?? [];

    public Task<TransactionDto> CreateTransactionAsync(
        long amountMinor,
        string currency,
        long? categoryId,
        string? note,
        TransactionType type = TransactionType.Expense,
        IEnumerable<long>? tagIds = null,
        long? walletId = null,
        string? idempotencyKey = null,
        DateTime? occurredAtUtc = null,
        CancellationToken ct = default) =>
        SendAsync<TransactionDto>(HttpMethod.Post, "api/v1/transactions", new
        {
            AmountMinor = amountMinor,
            Currency = currency,
            CategoryId = categoryId,
            OccurredAtUtc = occurredAtUtc,
            Note = note,
            Type = type,
            TagIds = tagIds?.ToList(),
            WalletId = walletId
        }, ct, idempotencyKey);

    public Task<TransactionDto> UpdateTransactionAsync(
        long id,
        long amountMinor,
        string currency,
        long? categoryId,
        string? note,
        TransactionType? type = null,
        DateTime? occurredAtUtc = null,
        IEnumerable<long>? tagIds = null,
        CancellationToken ct = default) =>
        SendAsync<TransactionDto>(HttpMethod.Put, $"api/v1/transactions/{id}", new
        {
            AmountMinor = amountMinor,
            Currency = currency,
            CategoryId = categoryId,
            Note = note,
            Type = type,
            OccurredAtUtc = occurredAtUtc,
            TagIds = tagIds?.ToList()
        }, ct);

    public Task DeleteTransactionAsync(long id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/v1/transactions/{id}", null, ct);

    public Task<BulkCreateResponse> BulkCreateTransactionsAsync(
        IReadOnlyList<BulkTransactionRequest> requests,
        CancellationToken ct = default) =>
        SendAsync<BulkCreateResponse>(HttpMethod.Post, "api/v1/transactions/bulk", requests, ct);

    /// <param name="mode">"all", "30days" or "month".</param>
    /// <param name="currency">Currency all totals are converted into.</param>
    public async Task<UserSummaryDto> GetSummaryAsync(
        string mode,
        string currency,
        CancellationToken ct = default)
    {
        var url = $"api/v1/transactions/summary?mode={Uri.EscapeDataString(mode)}&currency={Uri.EscapeDataString(currency)}";
        return await GetAsync<UserSummaryDto>(url, ct) ?? UserSummaryDto.Empty(currency);
    }

    public Task<byte[]> ExportCsvAsync(CancellationToken ct = default) =>
        http.GetByteArrayAsync("api/v1/transactions/export", ct);

    // Recurring transactions

    public async Task<List<RecurringTransactionDto>> GetRecurringTransactionsAsync(CancellationToken ct = default) =>
        await GetAsync<List<RecurringTransactionDto>>("api/v1/recurring", ct) ?? [];

    public Task<RecurringTransactionDto> CreateRecurringTransactionAsync(
        long amountMinor,
        string currency,
        long? categoryId,
        string? note,
        string pattern,
        TransactionType type,
        CancellationToken ct = default) =>
        SendAsync<RecurringTransactionDto>(HttpMethod.Post, "api/v1/recurring", new
        {
            AmountMinor = amountMinor,
            Currency = currency,
            CategoryId = categoryId,
            Note = note,
            Pattern = pattern,
            Type = type
        }, ct);

    public Task<RecurringTransactionDto> UpdateRecurringTransactionAsync(
        long id,
        long amountMinor,
        string currency,
        long? categoryId,
        string? note,
        string pattern,
        TransactionType type,
        CancellationToken ct = default) =>
        SendAsync<RecurringTransactionDto>(HttpMethod.Put, $"api/v1/recurring/{id}", new
        {
            AmountMinor = amountMinor,
            Currency = currency,
            CategoryId = categoryId,
            // An empty note clears it; null would keep the old one.
            Note = note ?? "",
            IsActive = true,
            Pattern = pattern,
            Type = type
        }, ct);

    public Task ExecuteRecurringTransactionAsync(long id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"api/v1/recurring/{id}/execute", null, ct);

    public Task DeleteRecurringTransactionAsync(long id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/v1/recurring/{id}", null, ct);

    // Budgets

    public async Task<List<BudgetDto>> GetBudgetsAsync(CancellationToken ct = default) =>
        await GetAsync<List<BudgetDto>>("api/v1/budgets", ct) ?? [];

    public Task<BudgetDto> CreateBudgetAsync(
        long amountMinor,
        string currency,
        long? categoryId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        CancellationToken ct = default) =>
        SendAsync<BudgetDto>(HttpMethod.Post, "api/v1/budgets", new
        {
            AmountMinor = amountMinor,
            Currency = currency,
            CategoryId = categoryId,
            StartDateUtc = startDateUtc,
            EndDateUtc = endDateUtc
        }, ct);

    public Task<BudgetDto> UpdateBudgetAsync(
        long id,
        long amountMinor,
        string currency,
        long? categoryId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        CancellationToken ct = default) =>
        SendAsync<BudgetDto>(HttpMethod.Put, $"api/v1/budgets/{id}", new
        {
            AmountMinor = amountMinor,
            Currency = currency,
            CategoryId = categoryId,
            StartDateUtc = startDateUtc,
            EndDateUtc = endDateUtc
        }, ct);

    public Task DeleteBudgetAsync(long id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/v1/budgets/{id}", null, ct);

    // Exchange rates

    public async Task<List<ExchangeRateDto>> GetExchangeRatesAsync(CancellationToken ct = default) =>
        await GetAsync<List<ExchangeRateDto>>("api/v1/exchange/rates", ct) ?? [];

    // Profile

    public async Task<UserProfileDto> GetProfileAsync(CancellationToken ct = default) =>
        await GetAsync<UserProfileDto>("api/v1/profile", ct)
        ?? throw new InvalidOperationException("API returned an empty response.");

    public Task SetDefaultCurrencyAsync(string currency, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, "api/v1/profile/currency", new { Currency = currency }, ct);

    /// <param name="language">"en" or "ru".</param>
    public Task SetLanguageAsync(string language, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, "api/v1/profile/language", new { Language = language }, ct);

    public Task DeleteAccountAsync(CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, "api/v1/profile", null, ct);

    // Goals

    public async Task<List<GoalDto>> GetGoalsAsync(CancellationToken ct = default) =>
        await GetAsync<List<GoalDto>>("api/v1/goals", ct) ?? [];

    public Task<GoalDto> CreateGoalAsync(
        string name,
        long targetAmountMinor,
        string currency,
        long? categoryId,
        DateTime? targetDateUtc,
        CancellationToken ct = default) =>
        SendAsync<GoalDto>(HttpMethod.Post, "api/v1/goals", new
        {
            Name = name,
            TargetAmountMinor = targetAmountMinor,
            Currency = currency,
            CategoryId = categoryId,
            TargetDateUtc = targetDateUtc
        }, ct);

    public Task<GoalDto> UpdateGoalAsync(
        long id,
        string name,
        long targetAmountMinor,
        DateTime? targetDateUtc,
        CancellationToken ct = default) =>
        SendAsync<GoalDto>(HttpMethod.Put, $"api/v1/goals/{id}", new
        {
            Name = name,
            TargetAmountMinor = targetAmountMinor,
            TargetDateUtc = targetDateUtc,
            IsActive = true
        }, ct);

    public Task DeleteGoalAsync(long id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/v1/goals/{id}", null, ct);

    public Task ContributeToGoalAsync(long id, long amountMinor, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"api/v1/goals/{id}/contribute", new { AmountMinor = amountMinor }, ct);

    // Tags

    public async Task<List<TagDto>> GetTagsAsync(CancellationToken ct = default) =>
        await GetAsync<List<TagDto>>("api/v1/tags", ct) ?? [];

    public Task<TagDto> CreateTagAsync(string name, string color = "#36A2EB", CancellationToken ct = default) =>
        SendAsync<TagDto>(HttpMethod.Post, "api/v1/tags", new { Name = name, Color = color }, ct);

    public Task<TagDto> UpdateTagAsync(long id, string name, string color, CancellationToken ct = default) =>
        SendAsync<TagDto>(HttpMethod.Put, $"api/v1/tags/{id}", new { Name = name, Color = color }, ct);

    public Task DeleteTagAsync(long id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/v1/tags/{id}", null, ct);

    // Shared wallets

    public async Task<List<WalletDto>> GetWalletsAsync(CancellationToken ct = default) =>
        await GetAsync<List<WalletDto>>("api/v1/wallets", ct) ?? [];

    public Task<WalletDto> CreateWalletAsync(string name, CancellationToken ct = default) =>
        SendAsync<WalletDto>(HttpMethod.Post, "api/v1/wallets", new { Name = name }, ct);

    public Task<WalletDto> JoinWalletAsync(string code, CancellationToken ct = default) =>
        SendAsync<WalletDto>(HttpMethod.Post, "api/v1/wallets/join", new { Code = code }, ct);

    public Task<WalletDto> RegenerateWalletInviteAsync(long id, CancellationToken ct = default) =>
        SendAsync<WalletDto>(HttpMethod.Post, $"api/v1/wallets/{id}/invite", null, ct);

    public Task LeaveWalletAsync(long id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"api/v1/wallets/{id}/leave", null, ct);

    public Task DeleteWalletAsync(long id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/v1/wallets/{id}", null, ct);

    public Task RemoveWalletMemberAsync(long id, long memberUserId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/v1/wallets/{id}/members/{memberUserId}", null, ct);

    /// <summary>Members, this month's summary in <paramref name="currency"/> and the latest transactions.</summary>
    public async Task<WalletView> GetWalletViewAsync(long id, string currency, CancellationToken ct = default)
    {
        var detailsTask = GetAsync<WalletDetailsDto>($"api/v1/wallets/{id}", ct);
        var summaryTask = GetAsync<WalletSummaryDto>(
            $"api/v1/wallets/{id}/summary?mode=month&currency={Uri.EscapeDataString(currency)}", ct);
        var transactionsTask = GetAsync<List<WalletTransactionDto>>($"api/v1/wallets/{id}/transactions?limit=20", ct);

        await Task.WhenAll(detailsTask, summaryTask, transactionsTask);

        return new WalletView(
            detailsTask.Result ?? throw new InvalidOperationException("API returned an empty response."),
            summaryTask.Result ?? throw new InvalidOperationException("API returned an empty response."),
            transactionsTask.Result ?? []);
    }

    // Helpers

    /// <summary>GET that reports failures as <see cref="ApiException"/> with the server's message.</summary>
    private async Task<T?> GetAsync<T>(string url, CancellationToken ct)
    {
        using var response = await SendCoreAsync(HttpMethod.Get, url, null, ct);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
    }

    private async Task SendAsync(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        using var response = await SendCoreAsync(method, url, body, ct);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body, CancellationToken ct,
        string? idempotencyKey = null)
    {
        using var response = await SendCoreAsync(method, url, body, ct, idempotencyKey);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct)
               ?? throw new InvalidOperationException("API returned an empty response.");
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string url, object? body, CancellationToken ct,
        string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType());

        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);

        var response = await http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
            return response;

        var details = await response.Content.ReadAsStringAsync(ct);
        response.Dispose();
        throw new ApiException((int)response.StatusCode, ApiException.ExtractMessage(details));
    }
}

/// <summary>An API error with the server's message, suitable for showing in the UI.</summary>
public sealed class ApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    internal static string ExtractMessage(string body)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(body);
            var root = document.RootElement;

            // ProblemDetails (RFC 9457): "detail" holds the human-readable reason.
            if (root.TryGetProperty("detail", out var detail) && detail.GetString() is { Length: > 0 } reason)
                return reason;

            if (root.TryGetProperty("error", out var error) && error.GetString() is { } message)
                return message;

            if (root.TryGetProperty("title", out var title) && title.GetString() is { } problem)
                return problem;
        }
        catch (System.Text.Json.JsonException)
        {
        }

        return string.IsNullOrWhiteSpace(body) ? "Request failed." : body;
    }
}
