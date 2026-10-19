using ExpensesBot.Api.Contracts.Transactions;

namespace ExpensesBot.Api.Contracts.Wallets;

public sealed record CreateWalletRequest(string? Name);

public sealed record RenameWalletRequest(string? Name);

public sealed record JoinWalletRequest(string? Code);

/// <param name="Role">"Owner" or "Member".</param>
/// <param name="InviteCode">Only shown to the owner, who manages membership.</param>
public sealed record WalletResponse(
    long Id,
    string Name,
    string Role,
    int MemberCount,
    string? InviteCode,
    DateTime CreatedAtUtc);

public sealed record WalletMemberResponse(
    long UserId,
    string DisplayName,
    string Role,
    DateTime JoinedAtUtc,
    bool IsYou);

public sealed record WalletDetailsResponse(
    long Id,
    string Name,
    string Role,
    string? InviteCode,
    DateTime CreatedAtUtc,
    List<WalletMemberResponse> Members);

/// <summary>A transaction in a shared wallet, with its author.</summary>
public sealed record WalletTransactionResponse(
    long Id,
    long AuthorUserId,
    string AuthorName,
    bool IsYours,
    long AmountMinor,
    string Currency,
    TransactionType Type,
    string? CategoryName,
    string? CategoryIcon,
    string? Note,
    DateTime OccurredAtUtc);

/// <summary>One member's share, converted to the summary currency.</summary>
public sealed record WalletMemberTotal(
    long UserId,
    string DisplayName,
    long IncomeMinor,
    long ExpenseMinor,
    int TransactionCount);

/// <summary>Expenses per category name (members' categories with the same name are merged).</summary>
public sealed record WalletCategoryTotal(
    string CategoryName,
    long ExpenseMinor);

public sealed record WalletSummaryResponse(
    string Currency,
    string Mode,
    long TotalIncomeMinor,
    long TotalExpenseMinor,
    long BalanceMinor,
    List<WalletMemberTotal> ByMember,
    List<WalletCategoryTotal> ByCategory,
    bool UsesFallbackRates);
