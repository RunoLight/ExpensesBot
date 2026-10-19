using System.Globalization;
using System.Text;
using ExpensesBot.Api.Common;
using ExpensesBot.Api.Contracts.Transactions;
using ExpensesBot.Data;
using ExpensesBot.Data.Entities;
using Microsoft.EntityFrameworkCore;
using ContractTransactionType = ExpensesBot.Api.Contracts.Transactions.TransactionType;
using EntityTransactionType = ExpensesBot.Data.Entities.TransactionType;

namespace ExpensesBot.Api.Application;

public sealed class TransactionService(ExpensesDbContext db)
{
    public const int MaxBulkSize = 100;
    public const string NoCategoryName = "Без категории";

    /// <param name="tagId">Only transactions marked with this tag, when set.</param>
    public Task<List<TransactionResponse>> GetPageAsync(long userId, int limit, int offset, long? tagId, CancellationToken ct)
    {
        var query = db.Transactions
            .AsNoTracking()
            .Where(x => x.UserId == userId);

        if (tagId.HasValue)
            query = query.Where(x => x.TransactionTags.Any(tt => tt.TagId == tagId.Value));

        // Order and page on the entity, then project: EF cannot translate
        // ordering by members of a record created with a constructor.
        var page = query
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip(Math.Max(offset, 0))
            .Take(Math.Clamp(limit, 1, 100));

        return Project(page).ToListAsync(ct);
    }

    public async Task<Result<TransactionResponse>> GetAsync(long userId, long id, CancellationToken ct)
    {
        var transaction = await Project(db.Transactions.AsNoTracking().Where(x => x.Id == id && x.UserId == userId))
            .FirstOrDefaultAsync(ct);

        return transaction is null ? Error.NotFound() : transaction;
    }

    /// <param name="idempotencyKey">
    /// Optional client-generated key. Repeating a request with the same key returns the
    /// transaction created the first time instead of recording a duplicate; reusing the
    /// key for a different amount, currency, type, category or wallet is rejected.
    /// </param>
    public async Task<Result<TransactionResponse>> CreateAsync(
        long userId,
        CreateTransactionRequest request,
        CancellationToken ct,
        string? idempotencyKey = null)
    {
        if (idempotencyKey is not null)
        {
            if (Rules.ValidateIdempotencyKey(idempotencyKey) is { } keyError)
                return keyError;

            if (await FindReplayAsync(userId, idempotencyKey, request, ct) is { } replay)
                return replay;
        }

        var built = await BuildAsync(userId, request, ct);
        if (!built.IsSuccess)
            return built.Error!;

        var (transaction, categoryName) = built.Value;
        transaction.IdempotencyKey = idempotencyKey;
        db.Transactions.Add(transaction);
        await WalletNotifications.QueueTransactionAsync(db, transaction, categoryName, transaction.CreatedAtUtc, ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (idempotencyKey is not null)
        {
            // A concurrent request with the same key won the unique index: answer with its result.
            db.ChangeTracker.Clear();
            if (await FindReplayAsync(userId, idempotencyKey, request, ct) is { } replay)
                return replay;

            throw;
        }

        return ToResponse(transaction, categoryName);
    }

    /// <summary>The earlier result for this key, a mismatch error, or null when the key is new.</summary>
    private async Task<Result<TransactionResponse>?> FindReplayAsync(
        long userId,
        string idempotencyKey,
        CreateTransactionRequest request,
        CancellationToken ct)
    {
        var existing = await db.Transactions
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.IdempotencyKey == idempotencyKey)
            .Select(x => new { x.Id, x.AmountMinor, x.Currency, x.Type, x.CategoryId, x.WalletId })
            .FirstOrDefaultAsync(ct);

        if (existing is null)
            return null;

        var sameRequest = existing.AmountMinor == request.AmountMinor &&
                          string.Equals(existing.Currency, request.Currency?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                          existing.Type == (EntityTransactionType)request.Type &&
                          existing.CategoryId == request.CategoryId &&
                          existing.WalletId == request.WalletId;

        if (!sameRequest)
            return Error.Unprocessable("This Idempotency-Key was already used for a different transaction.");

        return await GetAsync(userId, existing.Id, ct);
    }

    /// <summary>
    /// Validates every row; valid rows are saved together in one transaction and
    /// invalid ones are reported by index.
    /// </summary>
    public async Task<Result<BulkCreateResponse>> BulkCreateAsync(
        long userId,
        List<CreateTransactionRequest>? requests,
        CancellationToken ct)
    {
        if (requests is null || requests.Count == 0)
            return Error.Validation("No transactions provided.");

        if (requests.Count > MaxBulkSize)
            return Error.Validation($"Maximum {MaxBulkSize} transactions per batch.");

        var accepted = new List<(Transaction Transaction, string? CategoryName)>();
        var errors = new List<BulkError>();

        for (var i = 0; i < requests.Count; i++)
        {
            var built = await BuildAsync(userId, requests[i], ct);
            if (built.IsSuccess)
                accepted.Add(built.Value);
            else
                errors.Add(new BulkError(i, built.Error!.Message));
        }

        db.Transactions.AddRange(accepted.Select(x => x.Transaction));
        await db.SaveChangesAsync(ct);

        var created = accepted.Select(x => ToResponse(x.Transaction, x.CategoryName)).ToList();
        return new BulkCreateResponse(created, errors, created.Count, errors.Count);
    }

    public async Task<Result<TransactionResponse>> UpdateAsync(
        long userId,
        long id,
        UpdateTransactionRequest request,
        CancellationToken ct)
    {
        if (Rules.ValidateAmount(request.AmountMinor) is { } amountError)
            return amountError;

        if (Rules.ValidateCurrency(request.Currency, out var currency) is { } currencyError)
            return currencyError;

        if (Rules.ValidateNote(request.Note, out var note) is { } noteError)
            return noteError;

        var transaction = await db.Transactions
            .Include(x => x.TransactionTags)
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);

        if (transaction is null)
            return Error.NotFound();

        Category? category = null;
        if (request.CategoryId.HasValue)
        {
            // An archived category may be kept if the transaction already uses it.
            category = await db.Categories.FirstOrDefaultAsync(
                x => x.Id == request.CategoryId.Value &&
                     x.UserId == userId &&
                     (!x.IsArchived || x.Id == transaction.CategoryId),
                ct);

            if (category is null)
                return Error.Validation("Category does not exist or is unavailable.");
        }

        transaction.AmountMinor = request.AmountMinor;
        transaction.Currency = currency;
        transaction.CategoryId = category?.Id;
        transaction.Note = note;

        if (request.Type.HasValue)
            transaction.Type = (EntityTransactionType)request.Type.Value;

        await db.SaveChangesAsync(ct);
        return ToResponse(transaction, category?.Name);
    }

    public async Task<Result> DeleteAsync(long userId, long id, CancellationToken ct)
    {
        var deleted = await db.Transactions
            .Where(x => x.Id == id && x.UserId == userId)
            .ExecuteDeleteAsync(ct);

        return deleted == 0 ? Error.NotFound() : Result.Success();
    }

    /// <summary>
    /// CSV export. The column layout matches what the client's import expects:
    /// Date (UTC, yyyy-MM-dd HH:mm), Type, Category, Amount, Currency, Note.
    /// UTF-8 with BOM so Excel shows Cyrillic correctly.
    /// </summary>
    public async Task<byte[]> ExportCsvAsync(long userId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var query = db.Transactions.AsNoTracking().Where(x => x.UserId == userId);

        if (Rules.ToUtc(from) is { } fromUtc)
            query = query.Where(x => x.OccurredAtUtc >= fromUtc);

        if (Rules.ToUtc(to) is { } toUtc)
            query = query.Where(x => x.OccurredAtUtc <= toUtc);

        var rows = await query
            .OrderByDescending(x => x.OccurredAtUtc)
            .Select(x => new
            {
                x.OccurredAtUtc,
                x.Currency,
                x.AmountMinor,
                CategoryName = x.Category != null ? x.Category.Name : NoCategoryName,
                x.Note,
                x.Type
            })
            .ToListAsync(ct);

        var csv = new StringBuilder();
        csv.AppendLine("Дата (UTC),Тип,Категория,Сумма,Валюта,Комментарий");

        foreach (var row in rows)
        {
            csv.Append(row.OccurredAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append(',')
               .Append(row.Type == EntityTransactionType.Income ? "Доход" : "Расход").Append(',')
               .Append(CsvField(row.CategoryName)).Append(',')
               .Append((row.AmountMinor / 100m).ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
               .Append(row.Currency).Append(',')
               .Append(CsvField(row.Note))
               .AppendLine();
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    private async Task<Result<(Transaction Transaction, string? CategoryName)>> BuildAsync(
        long userId,
        CreateTransactionRequest request,
        CancellationToken ct)
    {
        if (Rules.ValidateAmount(request.AmountMinor) is { } amountError)
            return amountError;

        if (Rules.ValidateCurrency(request.Currency, out var currency) is { } currencyError)
            return currencyError;

        if (!Enum.IsDefined(request.Type))
            return Error.Validation("Unknown transaction type.");

        if (Rules.ValidateNote(request.Note, out var note) is { } noteError)
            return noteError;

        var occurredAt = request.OccurredAtUtc ?? DateTime.UtcNow;
        if (occurredAt.Kind == DateTimeKind.Local)
            return Error.Validation("OccurredAtUtc must be specified in UTC.");

        Category? category = null;
        if (request.CategoryId is not null)
        {
            category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(
                x => x.Id == request.CategoryId && x.UserId == userId && !x.IsArchived,
                ct);

            if (category is null)
                return Error.Validation("Category does not exist or is unavailable.");
        }

        // Recording into a shared wallet requires being its member.
        if (request.WalletId is { } walletId &&
            !await db.WalletMembers.AnyAsync(m => m.WalletId == walletId && m.UserId == userId, ct))
        {
            return Error.Validation("Wallet does not exist or you are not its member.");
        }

        var transaction = new Transaction
        {
            UserId = userId,
            CategoryId = category?.Id,
            AmountMinor = request.AmountMinor,
            Currency = currency,
            OccurredAtUtc = DateTime.SpecifyKind(occurredAt, DateTimeKind.Utc),
            CreatedAtUtc = DateTime.UtcNow,
            Note = note,
            Type = (EntityTransactionType)request.Type,
            WalletId = request.WalletId
        };

        if (request.TagIds is { Count: > 0 })
        {
            var tagIds = await db.Tags
                .Where(t => t.UserId == userId && request.TagIds.Contains(t.Id))
                .Select(t => t.Id)
                .ToListAsync(ct);

            transaction.TransactionTags.AddRange(tagIds.Select(tagId => new TransactionTag { TagId = tagId }));
        }

        return (transaction, category?.Name);
    }

    private static IQueryable<TransactionResponse> Project(IQueryable<Transaction> query) =>
        query.Select(x => new TransactionResponse(
            x.Id,
            x.AmountMinor,
            x.Currency,
            x.CategoryId,
            x.Category != null ? x.Category.Name : null,
            x.OccurredAtUtc,
            x.CreatedAtUtc,
            x.Note,
            (ContractTransactionType)x.Type,
            x.TransactionTags
                .Select(tt => new TransactionTagResponse(tt.TransactionId, tt.TagId))
                .ToList(),
            x.WalletId));

    private static TransactionResponse ToResponse(Transaction transaction, string? categoryName) =>
        new(
            transaction.Id,
            transaction.AmountMinor,
            transaction.Currency,
            transaction.CategoryId,
            categoryName,
            transaction.OccurredAtUtc,
            transaction.CreatedAtUtc,
            transaction.Note,
            (ContractTransactionType)transaction.Type,
            transaction.TransactionTags
                .Select(tt => new TransactionTagResponse(transaction.Id, tt.TagId))
                .ToList(),
            transaction.WalletId);

    /// <summary>
    /// Quotes a CSV field and neutralises spreadsheet formulas
    /// (cells starting with = + - @ are executed by Excel).
    /// </summary>
    private static string CsvField(string? value)
    {
        value ??= string.Empty;

        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            value = "'" + value;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
