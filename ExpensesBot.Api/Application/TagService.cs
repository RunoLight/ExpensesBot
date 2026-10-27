using System.Text.RegularExpressions;
using ExpensesBot.Api.Common;
using ExpensesBot.Api.Contracts.Tags;
using ExpensesBot.Data;
using ExpensesBot.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpensesBot.Api.Application;

public sealed partial class TagService(ExpensesDbContext db, TimeProvider clock)
{
    private const string DefaultColor = "#36A2EB";

    public Task<List<TagResponse>> GetAllAsync(long userId, CancellationToken ct) =>
        db.Tags
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new TagResponse(x.Id, x.Name, x.Color, x.CreatedAtUtc))
            .ToListAsync(ct);

    public async Task<Result<TagResponse>> CreateAsync(long userId, CreateTagRequest request, CancellationToken ct)
    {
        if (ValidateName(request.Name, out var name) is { } nameError)
            return nameError;

        if (await NameTakenAsync(userId, name, exceptId: null, ct))
            return Error.Conflict("Tag with this name already exists.");

        var tag = new Tag
        {
            UserId = userId,
            Name = name,
            Color = NormalizeColor(request.Color) ?? DefaultColor,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime
        };

        db.Tags.Add(tag);
        await db.SaveChangesAsync(ct);

        return ToResponse(tag);
    }

    /// <summary>Renames or recolours a tag. An invalid colour keeps the current one.</summary>
    public async Task<Result<TagResponse>> UpdateAsync(long userId, long id, UpdateTagRequest request, CancellationToken ct)
    {
        var tag = await db.Tags.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (tag is null)
            return Error.NotFound();

        if (ValidateName(request.Name, out var name) is { } nameError)
            return nameError;

        if (await NameTakenAsync(userId, name, exceptId: id, ct))
            return Error.Conflict("Tag with this name already exists.");

        tag.Name = name;
        tag.Color = NormalizeColor(request.Color) ?? tag.Color;

        await db.SaveChangesAsync(ct);
        return ToResponse(tag);
    }

    public async Task<Result> DeleteAsync(long userId, long id, CancellationToken ct)
    {
        var deleted = await db.Tags
            .Where(x => x.Id == id && x.UserId == userId)
            .ExecuteDeleteAsync(ct);

        return deleted == 0 ? Error.NotFound() : Result.Success();
    }

    private static Error? ValidateName(string? input, out string name)
    {
        name = input?.Trim() ?? string.Empty;
        return name.Length is 0 or > Rules.MaxTagNameLength
            ? Error.Validation($"Tag name must be 1-{Rules.MaxTagNameLength} characters.")
            : null;
    }

    private static string? NormalizeColor(string? color) =>
        color is not null && HexColor().IsMatch(color) ? color.ToUpperInvariant() : null;

    /// <summary>SQLite compares strings case-sensitively, so compare normalised names in memory.</summary>
    private async Task<bool> NameTakenAsync(long userId, string name, long? exceptId, CancellationToken ct)
    {
        var existingNames = await db.Tags
            .Where(x => x.UserId == userId && x.Id != exceptId)
            .Select(x => x.Name)
            .ToListAsync(ct);

        return existingNames.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
    }

    private static TagResponse ToResponse(Tag tag) => new(tag.Id, tag.Name, tag.Color, tag.CreatedAtUtc);

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();
}
