using System.Net;
using System.Net.Http.Json;
using ExpensesBot.Api.Application;
using ExpensesBot.Api.Common;
using ExpensesBot.Api.Contracts.Transactions;
using ExpensesBot.Api.Contracts.Wallets;
using ExpensesBot.Api.Services;
using ExpensesBot.Api.Tests.Infrastructure;
using ExpensesBot.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExpensesBot.Api.Tests;

public class WalletServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
    private readonly TestDatabase _database = new();

    private WalletService CreateService(ExpensesDbContext db) =>
        new(db, new ExchangeRateProvider(db), new FixedTimeProvider(Now));

    [Fact]
    public async Task Create_MakesTheCallerOwner_WithAnInviteCode()
    {
        var owner = await _database.AddUserAsync(1);
        await using var db = _database.CreateContext();

        var wallet = (await CreateService(db).CreateAsync(owner.Id, new CreateWalletRequest("  Семья  "), default)).Value!;

        wallet.Name.Should().Be("Семья");
        wallet.Role.Should().Be("Owner");
        wallet.MemberCount.Should().Be(1);
        WalletInvites.Normalize(wallet.InviteCode).Should().Be(wallet.InviteCode);
    }

    [Fact]
    public async Task Join_WithCode_IsCaseInsensitiveAndIdempotent()
    {
        var owner = await _database.AddUserAsync(1);
        var member = await _database.AddUserAsync(2);
        await using var db = _database.CreateContext();
        var service = CreateService(db);
        var wallet = (await service.CreateAsync(owner.Id, new CreateWalletRequest("Семья"), default)).Value!;

        var joined = await service.JoinAsync(member.Id, new JoinWalletRequest(" " + wallet.InviteCode!.ToLowerInvariant()), default);
        var again = await service.JoinAsync(member.Id, new JoinWalletRequest(wallet.InviteCode), default);

        joined.Value!.Role.Should().Be("Member");
        joined.Value.InviteCode.Should().BeNull("only the owner sees the invite code");
        again.IsSuccess.Should().BeTrue();
        (await db.WalletMembers.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Join_UnknownCode_IsNotFound()
    {
        var user = await _database.AddUserAsync(1);
        await using var db = _database.CreateContext();

        var result = await CreateService(db).JoinAsync(user.Id, new JoinWalletRequest("ZZZZ2222"), default);

        result.Error!.Kind.Should().Be(ErrorKind.NotFound);
    }

    [Fact]
    public async Task Members_CannotManage_AndStrangersCannotSee()
    {
        var owner = await _database.AddUserAsync(1);
        var member = await _database.AddUserAsync(2);
        var stranger = await _database.AddUserAsync(3);
        await using var db = _database.CreateContext();
        var service = CreateService(db);
        var wallet = (await service.CreateAsync(owner.Id, new CreateWalletRequest("Семья"), default)).Value!;
        await service.JoinAsync(member.Id, new JoinWalletRequest(wallet.InviteCode), default);

        (await service.RegenerateInviteAsync(member.Id, wallet.Id, default)).Error!.Kind.Should().Be(ErrorKind.Forbidden);
        (await service.RemoveMemberAsync(member.Id, wallet.Id, owner.Id, default)).Error!.Kind.Should().Be(ErrorKind.Forbidden);
        (await service.GetAsync(stranger.Id, wallet.Id, default)).Error!.Kind.Should().Be(ErrorKind.NotFound);
        (await service.GetTransactionsAsync(stranger.Id, wallet.Id, 50, 0, default)).Error!.Kind.Should().Be(ErrorKind.NotFound);
        (await service.LeaveAsync(owner.Id, wallet.Id, default)).Error!.Kind.Should().Be(ErrorKind.Conflict);
    }

    [Fact]
    public async Task OnlyMembers_CanRecordIntoTheWallet()
    {
        var owner = await _database.AddUserAsync(1);
        var stranger = await _database.AddUserAsync(2);
        await using var db = _database.CreateContext();
        var wallet = (await CreateService(db).CreateAsync(owner.Id, new CreateWalletRequest("Семья"), default)).Value!;
        var transactions = new TransactionService(db);

        var own = await transactions.CreateAsync(owner.Id,
            new CreateTransactionRequest(1_000, "AMD", null, null, "Хлеб", WalletId: wallet.Id), default);
        var foreign = await transactions.CreateAsync(stranger.Id,
            new CreateTransactionRequest(1_000, "AMD", null, null, "Чужое", WalletId: wallet.Id), default);

        own.Value!.WalletId.Should().Be(wallet.Id);
        foreign.Error!.Kind.Should().Be(ErrorKind.Validation);
    }

    [Fact]
    public async Task RecordingIntoTheWallet_QueuesAMessageForTheOtherMembers()
    {
        var owner = await _database.AddUserAsync(1);
        var member = await _database.AddUserAsync(2);
        await using var db = _database.CreateContext();
        var service = CreateService(db);
        var wallet = (await service.CreateAsync(owner.Id, new CreateWalletRequest("Семья"), default)).Value!;
        await service.JoinAsync(member.Id, new JoinWalletRequest(wallet.InviteCode!), default);

        await new TransactionService(db).CreateAsync(owner.Id,
            new CreateTransactionRequest(150_000, "AMD", null, null, "Продукты", WalletId: wallet.Id), default);
        await new TransactionService(db).CreateAsync(owner.Id,
            new CreateTransactionRequest(500, "AMD", null, null, "Личное"), default);

        var message = await db.OutboxMessages.SingleAsync();
        message.UserId.Should().Be(member.Id, "the author doesn't need to hear about their own entry");
        message.Kind.Should().Be(WalletNotifications.TransactionKind);
        message.Text.Should().Contain("Семья").And.Contain("1 500,00 AMD").And.Contain("Продукты");
    }

    [Fact]
    public async Task Summary_SplitsSpendingByMember_InOneCurrency()
    {
        var owner = await _database.AddUserAsync(1);
        var member = await _database.AddUserAsync(2);
        await using var db = _database.CreateContext();
        var service = CreateService(db);
        var wallet = (await service.CreateAsync(owner.Id, new CreateWalletRequest("Семья"), default)).Value!;
        await service.JoinAsync(member.Id, new JoinWalletRequest(wallet.InviteCode), default);

        await _database.AddAsync(
            new Data.Entities.Transaction { UserId = owner.Id, WalletId = wallet.Id, AmountMinor = 30_000, Currency = "AMD", OccurredAtUtc = Now.AddDays(-1) },
            new Data.Entities.Transaction { UserId = member.Id, WalletId = wallet.Id, AmountMinor = 10_000, Currency = "AMD", OccurredAtUtc = Now.AddDays(-2) },
            new Data.Entities.Transaction { UserId = member.Id, WalletId = wallet.Id, AmountMinor = 50_000, Currency = "AMD", Type = Data.Entities.TransactionType.Income, OccurredAtUtc = Now.AddDays(-2) },
            new Data.Entities.Transaction { UserId = member.Id, AmountMinor = 99_000, Currency = "AMD", OccurredAtUtc = Now.AddDays(-2) }); // personal

        var summary = (await service.GetSummaryAsync(owner.Id, wallet.Id, "AMD", "month", default)).Value!;

        summary.TotalExpenseMinor.Should().Be(40_000, "personal transactions are not part of the wallet");
        summary.TotalIncomeMinor.Should().Be(50_000);
        summary.ByMember.Should().HaveCount(2);
        summary.ByMember.Single(x => x.UserId == owner.Id).ExpenseMinor.Should().Be(30_000);
        summary.ByMember.Single(x => x.UserId == member.Id).ExpenseMinor.Should().Be(10_000);
    }

    [Fact]
    public async Task Leaving_TurnsTheMembersWalletTransactionsPersonal()
    {
        var owner = await _database.AddUserAsync(1);
        var member = await _database.AddUserAsync(2);
        await using var db = _database.CreateContext();
        var service = CreateService(db);
        var wallet = (await service.CreateAsync(owner.Id, new CreateWalletRequest("Семья"), default)).Value!;
        await service.JoinAsync(member.Id, new JoinWalletRequest(wallet.InviteCode), default);
        await _database.AddAsync(
            new Data.Entities.Transaction { UserId = member.Id, WalletId = wallet.Id, AmountMinor = 5_000, Currency = "AMD" },
            new Data.Entities.Transaction { UserId = owner.Id, WalletId = wallet.Id, AmountMinor = 7_000, Currency = "AMD" });

        (await service.LeaveAsync(member.Id, wallet.Id, default)).IsSuccess.Should().BeTrue();

        (await db.Transactions.Where(t => t.UserId == member.Id).Select(t => t.WalletId).SingleAsync()).Should().BeNull();
        (await db.Transactions.Where(t => t.UserId == owner.Id).Select(t => t.WalletId).SingleAsync()).Should().Be(wallet.Id);
        (await service.GetAllAsync(member.Id, default)).Should().BeEmpty();
    }

    public void Dispose() => _database.Dispose();
}

public class WalletInvitesTests
{
    [Theory]
    [InlineData("abcd2345", "ABCD2345")]
    [InlineData(" ABCD-2345 ", "ABCD2345")]
    [InlineData("ABCD234", null)]
    [InlineData("ABCD23450", null)]
    [InlineData("ABCD0345", null)] // 0 is not in the alphabet
    [InlineData("АБВГ2345", null)] // Cyrillic
    [InlineData(null, null)]
    public void Normalize_AcceptsOnlyWellFormedCodes(string? input, string? expected)
    {
        WalletInvites.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void NewCode_IsAlwaysValid()
    {
        for (var i = 0; i < 100; i++)
        {
            var code = WalletInvites.NewCode();
            WalletInvites.Normalize(code).Should().Be(code);
        }
    }
}

public class WalletEndpointsTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task SharedWallet_EndToEnd()
    {
        var owner = factory.CreateClientForDevUser(700_001);
        var member = factory.CreateClientForDevUser(700_002);
        var stranger = factory.CreateClientForDevUser(700_003);

        var create = await owner.PostAsJsonAsync("/api/v1/wallets", new { Name = "Семья" });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var wallet = (await create.Content.ReadFromJsonAsync<WalletResponse>())!;

        (await member.PostAsJsonAsync("/api/v1/wallets/join", new { Code = wallet.InviteCode }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var recorded = await member.PostAsJsonAsync("/api/v1/transactions",
            new { AmountMinor = 12_000, Currency = "AMD", Note = "Продукты", Type = 0, WalletId = wallet.Id });
        recorded.StatusCode.Should().Be(HttpStatusCode.Created);
        var transaction = (await recorded.Content.ReadFromJsonAsync<TransactionResponse>())!;

        // The owner sees the member's transaction with its author.
        var shared = await owner.GetFromJsonAsync<List<WalletTransactionResponse>>($"/api/v1/wallets/{wallet.Id}/transactions");
        shared.Should().ContainSingle().Which.IsYours.Should().BeFalse();

        // Strangers don't learn that the wallet exists; members may not manage it.
        (await stranger.GetAsync($"/api/v1/wallets/{wallet.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await member.DeleteAsync($"/api/v1/wallets/{wallet.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Deleting the wallet keeps the member's transaction as a personal one.
        (await owner.DeleteAsync($"/api/v1/wallets/{wallet.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();
        var kept = await db.Transactions.SingleAsync(t => t.Id == transaction.Id);
        kept.WalletId.Should().BeNull();
        (await db.WalletMembers.AnyAsync(m => m.WalletId == wallet.Id)).Should().BeFalse();
    }
}
