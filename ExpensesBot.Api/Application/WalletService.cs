using ExpensesBot.Api.Common;
using ExpensesBot.Api.Contracts.Wallets;
using ExpensesBot.Api.Services;
using ExpensesBot.Data;
using ExpensesBot.Data.Entities;
using Microsoft.EntityFrameworkCore;
using ContractTransactionType = ExpensesBot.Api.Contracts.Transactions.TransactionType;

namespace ExpensesBot.Api.Application;

/// <summary>
/// Shared wallets. Access is checked per wallet: non-members get 404 (the
/// wallet's existence isn't revealed), members can read and record, and only
/// the owner can rename, invite, remove members or delete the wallet (403).
/// </summary>
public sealed class WalletService(ExpensesDbContext db, ExchangeRateProvider exchangeRates, TimeProvider clock)
{
    public const int MaxWalletsPerUser = 10;
    public const int MaxNameLength = 80;

    public async Task<List<WalletResponse>> GetAllAsync(long userId, CancellationToken ct)
    {
        var rows = await db.WalletMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.Wallet.Name)
            .Select(m => new
            {
                m.Wallet.Id,
                m.Wallet.Name,
                m.Role,
                MemberCount = m.Wallet.Members.Count,
                m.Wallet.InviteCode,
                m.Wallet.CreatedAtUtc
            })
            .ToListAsync(ct);

        return rows
            .Select(r => new WalletResponse(
                r.Id,
                r.Name,
                r.Role.ToString(),
                r.MemberCount,
                r.Role == WalletRole.Owner ? r.InviteCode : null,
                r.CreatedAtUtc))
            .ToList();
    }

    public async Task<Result<WalletDetailsResponse>> GetAsync(long userId, long walletId, CancellationToken ct)
    {
        if (await RoleAsync(userId, walletId, ct) is not { } role)
            return Error.NotFound();

        var text = await db.TextForAsync(userId, ct);

        var wallet = await db.Wallets
            .AsNoTracking()
            .FirstAsync(w => w.Id == walletId, ct);

        var members = await db.WalletMembers
            .AsNoTracking()
            .Where(m => m.WalletId == walletId)
            .OrderBy(m => m.Role)
            .ThenBy(m => m.JoinedAtUtc)
            .Select(m => new { m.UserId, m.User.DisplayName, m.Role, m.JoinedAtUtc })
            .ToListAsync(ct);

        return new WalletDetailsResponse(
            wallet.Id,
            wallet.Name,
            role.ToString(),
            role == WalletRole.Owner ? wallet.InviteCode : null,
            wallet.CreatedAtUtc,
            members
                .Select(m => new WalletMemberResponse(
                    m.UserId, DisplayName(m.UserId, m.DisplayName, text), m.Role.ToString(), m.JoinedAtUtc, m.UserId == userId))
                .ToList());
    }

    public async Task<Result<WalletResponse>> CreateAsync(long userId, CreateWalletRequest request, CancellationToken ct)
    {
        if (ValidateName(request.Name, out var name) is { } nameError)
            return nameError;

        if (await db.WalletMembers.CountAsync(m => m.UserId == userId, ct) >= MaxWalletsPerUser)
            return Error.Conflict(t => t.TooManyWallets(MaxWalletsPerUser));

        var now = clock.GetUtcNow().UtcDateTime;
        var wallet = new Wallet
        {
            Name = name,
            InviteCode = await NewUniqueCodeAsync(ct),
            CreatedAtUtc = now,
            Members = [new WalletMember { UserId = userId, Role = WalletRole.Owner, JoinedAtUtc = now }]
        };

        db.Wallets.Add(wallet);
        await db.SaveChangesAsync(ct);

        return new WalletResponse(wallet.Id, wallet.Name, nameof(WalletRole.Owner), 1, wallet.InviteCode, wallet.CreatedAtUtc);
    }

    public async Task<Result<WalletResponse>> JoinAsync(long userId, JoinWalletRequest request, CancellationToken ct)
    {
        if (await db.WalletMembers.CountAsync(m => m.UserId == userId, ct) >= MaxWalletsPerUser)
            return Error.Conflict(t => t.TooManyWallets(MaxWalletsPerUser));

        var result = await WalletInvites.JoinAsync(db, userId, request.Code, clock.GetUtcNow().UtcDateTime, ct);

        if (result.Status == JoinWalletStatus.NotFound)
            return Error.NotFound(t => t.InviteNotFound);

        if (result.Status == JoinWalletStatus.Full)
            return Error.Conflict(t => t.WalletFull(WalletInvites.MaxMembers));

        // Joined now or earlier: either way the caller is a member.
        return (await GetAllAsync(userId, ct)).Single(w => w.Id == result.Wallet!.Id);
    }

    public async Task<Result<WalletResponse>> RenameAsync(long userId, long walletId, RenameWalletRequest request, CancellationToken ct)
    {
        if (await RequireOwnerAsync(userId, walletId, ct) is { } accessError)
            return accessError;

        if (ValidateName(request.Name, out var name) is { } nameError)
            return nameError;

        await db.Wallets
            .Where(w => w.Id == walletId)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Name, name), ct);

        return (await GetAllAsync(userId, ct)).Single(w => w.Id == walletId);
    }

    /// <summary>A new invite code; links with the old one stop working.</summary>
    public async Task<Result<WalletResponse>> RegenerateInviteAsync(long userId, long walletId, CancellationToken ct)
    {
        if (await RequireOwnerAsync(userId, walletId, ct) is { } accessError)
            return accessError;

        var code = await NewUniqueCodeAsync(ct);
        await db.Wallets
            .Where(w => w.Id == walletId)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.InviteCode, code), ct);

        return (await GetAllAsync(userId, ct)).Single(w => w.Id == walletId);
    }

    /// <summary>Deletes the wallet. Members' transactions stay, as personal ones (FK ON DELETE SET NULL).</summary>
    public async Task<Result> DeleteAsync(long userId, long walletId, CancellationToken ct)
    {
        if (await RequireOwnerAsync(userId, walletId, ct) is { } accessError)
            return accessError;

        await db.Wallets.Where(w => w.Id == walletId).ExecuteDeleteAsync(ct);
        return Result.Success();
    }

    public async Task<Result> LeaveAsync(long userId, long walletId, CancellationToken ct)
    {
        var role = await RoleAsync(userId, walletId, ct);
        if (role is null)
            return Error.NotFound();

        if (role == WalletRole.Owner)
            return Error.Conflict(t => t.OwnerCantLeave);

        await RemoveMembershipAsync(walletId, userId, ct);
        return Result.Success();
    }

    public async Task<Result> RemoveMemberAsync(long userId, long walletId, long memberUserId, CancellationToken ct)
    {
        if (await RequireOwnerAsync(userId, walletId, ct) is { } accessError)
            return accessError;

        if (memberUserId == userId)
            return Error.Validation(t => t.OwnerCantRemoveSelf);

        if (await RoleAsync(memberUserId, walletId, ct) is null)
            return Error.NotFound(t => t.MemberNotFound);

        await RemoveMembershipAsync(walletId, memberUserId, ct);
        return Result.Success();
    }

    public async Task<Result<List<WalletTransactionResponse>>> GetTransactionsAsync(
        long userId,
        long walletId,
        int limit,
        int offset,
        CancellationToken ct)
    {
        if (await RoleAsync(userId, walletId, ct) is null)
            return Error.NotFound();

        var text = await db.TextForAsync(userId, ct);

        var rows = await db.Transactions
            .AsNoTracking()
            .Where(t => t.WalletId == walletId)
            .OrderByDescending(t => t.OccurredAtUtc)
            .ThenByDescending(t => t.Id)
            .Skip(Math.Max(offset, 0))
            .Take(Math.Clamp(limit, 1, 100))
            .Select(t => new
            {
                t.Id,
                t.UserId,
                t.User.DisplayName,
                t.AmountMinor,
                t.Currency,
                t.Type,
                CategoryName = t.Category != null ? t.Category.Name : null,
                CategoryIcon = t.Category != null ? t.Category.Icon : null,
                t.Note,
                t.OccurredAtUtc
            })
            .ToListAsync(ct);

        return rows
            .Select(t => new WalletTransactionResponse(
                t.Id,
                t.UserId,
                DisplayName(t.UserId, t.DisplayName, text),
                t.UserId == userId,
                t.AmountMinor,
                t.Currency,
                (ContractTransactionType)t.Type,
                t.CategoryName,
                t.CategoryIcon,
                t.Note,
                t.OccurredAtUtc))
            .ToList();
    }

    /// <summary>Income and expenses of the wallet per member and per category, in one currency.</summary>
    public async Task<Result<WalletSummaryResponse>> GetSummaryAsync(
        long userId,
        long walletId,
        string? currency,
        string? mode,
        CancellationToken ct)
    {
        if (await RoleAsync(userId, walletId, ct) is null)
            return Error.NotFound();

        var text = await db.TextForAsync(userId, ct);

        var target = Currencies.TryNormalize(currency, out var requested)
            ? requested
            : await db.Users.Where(u => u.Id == userId).Select(u => u.DefaultCurrency).FirstAsync(ct);

        var now = clock.GetUtcNow().UtcDateTime;
        var summaryMode = mode?.ToLowerInvariant() is { } m && SummaryService.Modes.Contains(m) ? m : "month";

        DateTime? fromUtc = summaryMode switch
        {
            "30days" => now.AddDays(-30),
            "month" => new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => null
        };

        var query = db.Transactions.AsNoTracking().Where(t => t.WalletId == walletId);
        if (fromUtc.HasValue)
            query = query.Where(t => t.OccurredAtUtc >= fromUtc.Value);

        var rows = await query
            .Select(t => new
            {
                t.UserId,
                t.Type,
                t.AmountMinor,
                t.Currency,
                CategoryName = t.Category != null ? t.Category.Name : null
            })
            .ToListAsync(ct);

        var members = await db.WalletMembers
            .AsNoTracking()
            .Where(x => x.WalletId == walletId)
            .OrderBy(x => x.JoinedAtUtc)
            .Select(x => new { x.UserId, x.User.DisplayName })
            .ToListAsync(ct);

        var rates = await exchangeRates.GetAsync(ct);
        var converted = rows
            .Select(r => new
            {
                r.UserId,
                r.Type,
                r.CategoryName,
                Amount = rates.Convert(r.AmountMinor, r.Currency, target) ?? 0
            })
            .ToList();

        var byMember = members
            .Select(member =>
            {
                var own = converted.Where(r => r.UserId == member.UserId).ToList();
                return new WalletMemberTotal(
                    member.UserId,
                    DisplayName(member.UserId, member.DisplayName, text),
                    own.Where(r => r.Type == TransactionType.Income).Sum(r => r.Amount),
                    own.Where(r => r.Type == TransactionType.Expense).Sum(r => r.Amount),
                    own.Count);
            })
            .OrderByDescending(x => x.ExpenseMinor)
            .ToList();

        var byCategory = converted
            .Where(r => r.Type == TransactionType.Expense)
            .GroupBy(r => r.CategoryName ?? text.NoCategory)
            .Select(g => new WalletCategoryTotal(g.Key, g.Sum(r => r.Amount)))
            .OrderByDescending(x => x.ExpenseMinor)
            .ToList();

        var income = converted.Where(r => r.Type == TransactionType.Income).Sum(r => r.Amount);
        var expense = converted.Where(r => r.Type == TransactionType.Expense).Sum(r => r.Amount);

        return new WalletSummaryResponse(
            target, summaryMode, income, expense, income - expense, byMember, byCategory, rates.IsFallback);
    }

    /// <summary>The caller's role in the wallet, or null when they are not a member.</summary>
    public async Task<WalletRole?> RoleAsync(long userId, long walletId, CancellationToken ct)
    {
        var membership = await db.WalletMembers
            .AsNoTracking()
            .Where(m => m.WalletId == walletId && m.UserId == userId)
            .Select(m => new { m.Role })
            .FirstOrDefaultAsync(ct);

        return membership?.Role;
    }

    private async Task<Error?> RequireOwnerAsync(long userId, long walletId, CancellationToken ct) =>
        await RoleAsync(userId, walletId, ct) switch
        {
            null => Error.NotFound(),
            WalletRole.Owner => null,
            _ => Error.Forbidden(t => t.OwnerOnly)
        };

    /// <summary>Removes a member; their transactions in the wallet become personal again.</summary>
    private async Task RemoveMembershipAsync(long walletId, long memberUserId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await db.Transactions
            .Where(t => t.WalletId == walletId && t.UserId == memberUserId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.WalletId, (long?)null), ct);

        await db.WalletMembers
            .Where(m => m.WalletId == walletId && m.UserId == memberUserId)
            .ExecuteDeleteAsync(ct);

        await transaction.CommitAsync(ct);
    }

    private async Task<string> NewUniqueCodeAsync(CancellationToken ct)
    {
        // 31^8 codes: a collision is practically impossible, but the unique index would reject one.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = WalletInvites.NewCode();
            if (!await db.Wallets.AnyAsync(w => w.InviteCode == code, ct))
                return code;
        }

        throw new InvalidOperationException("Could not generate a unique invite code.");
    }

    private static Error? ValidateName(string? input, out string name)
    {
        name = input?.Trim() ?? string.Empty;
        return name.Length is 0 or > MaxNameLength
            ? Error.Validation(t => t.NameLength(MaxNameLength))
            : null;
    }

    private static string DisplayName(long userId, string? displayName, MessageText text) =>
        string.IsNullOrWhiteSpace(displayName) ? text.MemberFallbackName(userId) : displayName;
}
