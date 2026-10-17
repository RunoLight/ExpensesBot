using ExpensesBot.Api.Common;
using ExpensesBot.Api.Contracts.Categories;
using ExpensesBot.Data;
using ExpensesBot.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpensesBot.Api.Application;

public sealed class CategoryService(ExpensesDbContext db)
{
    public Task<List<CategoryResponse>> GetAllAsync(long userId, CancellationToken ct) =>
        db.Categories
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.IsArchived)
            .ThenBy(x => x.Name)
            .Select(x => new CategoryResponse(x.Id, x.Name, x.Icon, x.IsArchived))
            .ToListAsync(ct);

    public async Task<Result<CategoryResponse>> CreateAsync(long userId, CreateCategoryRequest request, CancellationToken ct)
    {
        if (ValidateName(request.Name, out var name) is { } error)
            return error;

        var category = new Category
        {
            UserId = userId,
            Name = name,
            Icon = string.IsNullOrWhiteSpace(request.Icon) ? "📁" : request.Icon.Trim()
        };

        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);

        return ToResponse(category);
    }

    public async Task<Result<CategoryResponse>> UpdateAsync(long userId, long id, UpdateCategoryRequest request, CancellationToken ct)
    {
        var category = await FindAsync(userId, id, ct);
        if (category is null)
            return Error.NotFound();

        if (ValidateName(request.Name, out var name) is { } error)
            return error;

        category.Name = name;
        if (!string.IsNullOrWhiteSpace(request.Icon))
            category.Icon = request.Icon.Trim();

        await db.SaveChangesAsync(ct);
        return ToResponse(category);
    }

    /// <summary>Transactions in the category keep existing and become uncategorised.</summary>
    public async Task<Result> DeleteAsync(long userId, long id, CancellationToken ct)
    {
        var category = await FindAsync(userId, id, ct);
        if (category is null)
            return Error.NotFound();

        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<CategoryResponse>> ToggleArchiveAsync(long userId, long id, CancellationToken ct)
    {
        var category = await FindAsync(userId, id, ct);
        if (category is null)
            return Error.NotFound();

        category.IsArchived = !category.IsArchived;
        await db.SaveChangesAsync(ct);
        return ToResponse(category);
    }

    private Task<Category?> FindAsync(long userId, long id, CancellationToken ct) =>
        db.Categories.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);

    private static Error? ValidateName(string? input, out string name)
    {
        name = input?.Trim() ?? string.Empty;
        return name.Length is 0 or > Rules.MaxCategoryNameLength
            ? Error.Validation($"Name must be 1-{Rules.MaxCategoryNameLength} characters.")
            : null;
    }

    private static CategoryResponse ToResponse(Category category) =>
        new(category.Id, category.Name, category.Icon, category.IsArchived);
}
