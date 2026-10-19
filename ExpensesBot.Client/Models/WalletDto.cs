namespace ExpensesBot.Client.Models;

public sealed record WalletDto(
    long Id,
    string Name,
    string Role,
    int MemberCount,
    string? InviteCode,
    DateTime CreatedAtUtc)
{
    public bool IsOwner => Role == "Owner";
}

public sealed record WalletMemberDto(
    long UserId,
    string DisplayName,
    string Role,
    DateTime JoinedAtUtc,
    bool IsYou);

public sealed record WalletDetailsDto(
    long Id,
    string Name,
    string Role,
    string? InviteCode,
    DateTime CreatedAtUtc,
    List<WalletMemberDto> Members);

public sealed record WalletTransactionDto(
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

public sealed record WalletMemberTotalDto(
    long UserId,
    string DisplayName,
    long IncomeMinor,
    long ExpenseMinor,
    int TransactionCount);

public sealed record WalletCategoryTotalDto(string CategoryName, long ExpenseMinor);

public sealed record WalletSummaryDto(
    string Currency,
    string Mode,
    long TotalIncomeMinor,
    long TotalExpenseMinor,
    long BalanceMinor,
    List<WalletMemberTotalDto> ByMember,
    List<WalletCategoryTotalDto> ByCategory,
    bool UsesFallbackRates);

/// <summary>Everything the wallet panel shows for an opened wallet.</summary>
public sealed record WalletView(
    WalletDetailsDto Details,
    WalletSummaryDto Summary,
    List<WalletTransactionDto> Transactions);

/// <summary>A member the owner wants to remove.</summary>
public sealed record WalletMemberRemoval(long WalletId, long UserId);
