namespace ExpensesBot.Client.Models;

/// <summary>
/// Search, filters and order of the history. The API applies them, so they cover
/// every transaction, not only the pages loaded so far.
/// </summary>
/// <param name="TagIds">Transactions with at least one of these tags; empty for any.</param>
public sealed record HistoryFilter(
    string Search = "",
    long? CategoryId = null,
    IReadOnlyList<long>? TagIds = null,
    string Sort = HistoryFilter.DefaultSort)
{
    public const string DefaultSort = "date-desc";

    /// <summary>Transactions per page of the history.</summary>
    public const int PageSize = 50;

    public IReadOnlyList<long> Tags => TagIds ?? [];

    /// <summary>True when something narrows the list (the order alone doesn't).</summary>
    public bool IsFiltered => !string.IsNullOrWhiteSpace(Search) || CategoryId.HasValue || Tags.Count > 0;

    /// <summary>The query string for GET /api/v1/transactions, without paging.</summary>
    public string ToQueryString()
    {
        var parts = new List<string> { $"sort={Uri.EscapeDataString(Sort)}" };

        if (!string.IsNullOrWhiteSpace(Search))
            parts.Add($"search={Uri.EscapeDataString(Search.Trim())}");

        if (CategoryId is { } categoryId)
            parts.Add($"categoryId={categoryId}");

        parts.AddRange(Tags.Select(tagId => $"tagId={tagId}"));
        return string.Join("&", parts);
    }
}
