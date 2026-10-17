using System.Text.RegularExpressions;
using ExpensesBot.Api.Common;
using ExpensesBot.Api.Contracts.Tags;
using ExpensesBot.Data;
using ExpensesBot.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpensesBot.Api.Application;

public sealed partial class TagService(ExpensesDbContext db)
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
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > Rules.MaxTagNameLength)
            return Error.Validation($"Tag name must be 1-{Rules.MaxTagNameLength} characters.");

        var color = request.Color is not null && HexColor().IsMatch(request.Color)
            ? request.Color.ToUpperInvariant()
            : DefaultColor;

        // SQLite compares strings case-sensitively, so compare normalised names in memory.
        var existingNames = await db.Tags
            .Where(x => x.UserId == userId)
            .Select(x => x.Name)
            .ToListAsync(ct);

        if (existingNames.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)))
            return Error.Conflict("Tag with this name already exists.");

        var tag = new Tag
        {
            UserId = userId,
            Name = name,
            Color = color,
            CreatedAtUtc = DateTime.UtcNow
        };

        db.Tags.Add(tag);
        await db.SaveChangesAsync(ct);

        return new TagResponse(tag.Id, tag.Name, tag.Color, tag.CreatedAtUtc);
    }

    public async Task<Result> DeleteAsync(long userId, long id, CancellationToken ct)
    {
        var deleted = await db.Tags
            .Where(x => x.Id == id && x.UserId == userId)
            .ExecuteDeleteAsync(ct);

        return deleted == 0 ? Error.NotFound() : Result.Success();
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();
}
