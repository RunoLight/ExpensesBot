using ExpensesBot.Api.Common;
using ExpensesBot.Api.Contracts.RecurringTransactions;
using ExpensesBot.Api.Contracts.Transactions;
using ExpensesBot.Data;
using ExpensesBot.Data.Entities;
using Microsoft.EntityFrameworkCore;
using ContractTransactionType = ExpensesBot.Api.Contracts.Transactions.TransactionType;
using TransactionType = ExpensesBot.Data.Entities.TransactionType;

namespace ExpensesBot.Api.Application;

public sealed class RecurringTransactionService(ExpensesDbContext db, TimeProvider clock)
{
    public Task<List<RecurringTransactionResponse>> GetAllAsync(long userId, CancellationToken ct) =>
        db.RecurringTransactions
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new RecurringTransactionResponse(
                x.Id,
                x.AmountMinor,
                x.Currency,
                x.CategoryId,
                x.Category != null ? x.Category.Name : null,
                x.Note,
                x.Pattern.ToString(),
                x.EndsAfter,
                x.TimesExecuted,
                x.LastExecutedAtUtc,
                x.IsActive,
                x.CreatedAtUtc,
                (ContractTransactionType)x.Type))
            .ToListAsync(ct);

    public async Task<Result<RecurringTransactionResponse>> CreateAsync(
        long userId,
        CreateRecurringTransactionRequest request,
        CancellationToken ct)
    {
        if (Rules.ValidateAmount(request.AmountMinor) is { } amountError)
            return amountError;

        if (Rules.ValidateCurrency(request.Currency, out var currency) is { } currencyError)
            return currencyError;

        if (!Enum.IsDefined(request.Type))
            return Error.Validation(t => t.UnknownTransactionType);

        if (!TryParsePattern(request.Pattern, out var pattern))
            return Error.Validation(t => t.UnknownPattern);

        if (Rules.ValidateNote(request.Note, out var note) is { } noteError)
            return noteError;

        var category = await FindCategoryAsync(userId, request.CategoryId, ct);
        if (request.CategoryId.HasValue && category is null)
            return Error.Validation(t => t.CategoryNotFound);

        var recurring = new RecurringTransaction
        {
            UserId = userId,
            CategoryId = category?.Id,
            AmountMinor = request.AmountMinor,
            Currency = currency,
            Note = note,
            Pattern = pattern,
            Type = (TransactionType)request.Type,
            EndsAfter = Rules.ToUtc(request.EndsAfter),
            // The first automatic run happens one period from now.
            LastExecutedAtUtc = clock.GetUtcNow().UtcDateTime,
            IsActive = true
        };

        db.RecurringTransactions.Add(recurring);
        await db.SaveChangesAsync(ct);

        return ToResponse(recurring, category?.Name);
    }

    /// <summary>Partial update: amount/currency only when AmountMinor &gt; 0; note, schedule and type only when provided.</summary>
    public async Task<Result<RecurringTransactionResponse>> UpdateAsync(
        long userId,
        long id,
        UpdateRecurringTransactionRequest request,
        CancellationToken ct)
    {
        var recurring = await db.RecurringTransactions
            .Include(x => x.Category)
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);

        if (recurring is null)
            return Error.NotFound();

        if (request.Pattern is not null)
        {
            if (!TryParsePattern(request.Pattern, out var pattern))
                return Error.Validation(t => t.UnknownPattern);

            recurring.Pattern = pattern;
        }

        if (request.Type is { } type)
        {
            if (!Enum.IsDefined(type))
                return Error.Validation(t => t.UnknownTransactionType);

            recurring.Type = (TransactionType)type;
        }

        if (request.AmountMinor > 0)
        {
            if (Rules.ValidateAmount(request.AmountMinor) is { } amountError)
                return amountError;

            if (Rules.ValidateCurrency(request.Currency, out var currency) is { } currencyError)
                return currencyError;

            recurring.AmountMinor = request.AmountMinor;
            recurring.Currency = currency;
        }

        if (request.CategoryId.HasValue)
        {
            var category = await FindCategoryAsync(userId, request.CategoryId, ct);
            if (category is null)
                return Error.Validation(t => t.CategoryNotFound);

            recurring.CategoryId = category.Id;
            recurring.Category = category;
        }

        if (request.Note is not null)
        {
            if (Rules.ValidateNote(request.Note, out var note) is { } noteError)
                return noteError;

            recurring.Note = note;
        }

        recurring.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);

        return ToResponse(recurring, recurring.Category?.Name);
    }

    private static bool TryParsePattern(string? value, out RecurrencePattern pattern) =>
        Enum.TryParse(value, ignoreCase: true, out pattern) &&
        Enum.IsDefined(pattern) &&
        pattern != RecurrencePattern.None;

    public async Task<Result> DeleteAsync(long userId, long id, CancellationToken ct)
    {
        var deleted = await db.RecurringTransactions
            .Where(x => x.Id == id && x.UserId == userId)
            .ExecuteDeleteAsync(ct);

        return deleted == 0 ? Error.NotFound() : Result.Success();
    }

    /// <summary>Creates the transaction now, outside the schedule.</summary>
    public async Task<Result<TransactionResponse>> ExecuteNowAsync(long userId, long id, CancellationToken ct)
    {
        var recurring = await db.RecurringTransactions
            .Include(x => x.Category)
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);

        if (recurring is null)
            return Error.NotFound();

        if (!recurring.IsActive)
            return Error.Conflict(t => t.RecurringInactive);

        var now = clock.GetUtcNow().UtcDateTime;

        if (recurring.EndsAfter.HasValue && recurring.EndsAfter.Value <= now)
        {
            recurring.IsActive = false;
            await db.SaveChangesAsync(ct);
            return Error.Conflict(t => t.RecurringEnded);
        }

        var transaction = new Transaction
        {
            UserId = userId,
            CategoryId = recurring.CategoryId,
            AmountMinor = recurring.AmountMinor,
            Currency = recurring.Currency,
            Type = recurring.Type,
            Note = (await db.TextForAsync(userId, ct)).AutoNote(recurring.Note, recurring.Type),
            OccurredAtUtc = now,
            CreatedAtUtc = now
        };

        db.Transactions.Add(transaction);
        recurring.TimesExecuted++;
        recurring.LastExecutedAtUtc = now;
        await db.SaveChangesAsync(ct);

        return new TransactionResponse(
            transaction.Id,
            transaction.AmountMinor,
            transaction.Currency,
            transaction.CategoryId,
            recurring.Category?.Name,
            transaction.OccurredAtUtc,
            transaction.CreatedAtUtc,
            transaction.Note,
            (ContractTransactionType)transaction.Type,
            []);
    }

    private async Task<Category?> FindCategoryAsync(long userId, long? categoryId, CancellationToken ct) =>
        categoryId is null
            ? null
            : await db.Categories.FirstOrDefaultAsync(
                x => x.Id == categoryId.Value && x.UserId == userId && !x.IsArchived, ct);

    private static RecurringTransactionResponse ToResponse(RecurringTransaction recurring, string? categoryName) =>
        new(
            recurring.Id,
            recurring.AmountMinor,
            recurring.Currency,
            recurring.CategoryId,
            categoryName,
            recurring.Note,
            recurring.Pattern.ToString(),
            recurring.EndsAfter,
            recurring.TimesExecuted,
            recurring.LastExecutedAtUtc,
            recurring.IsActive,
            recurring.CreatedAtUtc,
            (ContractTransactionType)recurring.Type);
}
