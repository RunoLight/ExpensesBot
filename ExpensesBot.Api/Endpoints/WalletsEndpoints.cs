using System.Security.Claims;
using ExpensesBot.Api.Application;
using ExpensesBot.Api.Authentication;
using ExpensesBot.Api.Common;
using ExpensesBot.Api.Contracts.Wallets;

namespace ExpensesBot.Api.Endpoints;

public static class WalletsEndpoints
{
    public static IEndpointRouteBuilder MapWalletsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/wallets")
            .WithTags("Wallets")
            .RequireAuthorization();

        group.MapGet("/", (ClaimsPrincipal user, WalletService service, CancellationToken ct) =>
            service.GetAllAsync(user.GetUserId(), ct));

        group.MapPost("/", async (CreateWalletRequest request, ClaimsPrincipal user, WalletService service, CancellationToken ct) =>
            (await service.CreateAsync(user.GetUserId(), request, ct)).ToCreated(w => $"/api/wallets/{w.Id}"));

        group.MapPost("/join", async (JoinWalletRequest request, ClaimsPrincipal user, WalletService service, CancellationToken ct) =>
            (await service.JoinAsync(user.GetUserId(), request, ct)).ToOk());

        group.MapGet("/{id:long}", async (long id, ClaimsPrincipal user, WalletService service, CancellationToken ct) =>
            (await service.GetAsync(user.GetUserId(), id, ct)).ToOk());

        group.MapPut("/{id:long}", async (long id, RenameWalletRequest request, ClaimsPrincipal user, WalletService service, CancellationToken ct) =>
            (await service.RenameAsync(user.GetUserId(), id, request, ct)).ToOk());

        group.MapDelete("/{id:long}", async (long id, ClaimsPrincipal user, WalletService service, CancellationToken ct) =>
            (await service.DeleteAsync(user.GetUserId(), id, ct)).ToNoContent());

        group.MapPost("/{id:long}/invite", async (long id, ClaimsPrincipal user, WalletService service, CancellationToken ct) =>
            (await service.RegenerateInviteAsync(user.GetUserId(), id, ct)).ToOk());

        group.MapPost("/{id:long}/leave", async (long id, ClaimsPrincipal user, WalletService service, CancellationToken ct) =>
            (await service.LeaveAsync(user.GetUserId(), id, ct)).ToNoContent());

        group.MapDelete("/{id:long}/members/{memberId:long}", async (long id, long memberId, ClaimsPrincipal user, WalletService service, CancellationToken ct) =>
            (await service.RemoveMemberAsync(user.GetUserId(), id, memberId, ct)).ToNoContent());

        group.MapGet("/{id:long}/transactions", async (long id, ClaimsPrincipal user, WalletService service, CancellationToken ct,
                int limit = 50, int offset = 0) =>
            (await service.GetTransactionsAsync(user.GetUserId(), id, limit, offset, ct)).ToOk());

        group.MapGet("/{id:long}/summary", async (long id, ClaimsPrincipal user, WalletService service, CancellationToken ct,
                string? currency = null, string? mode = null) =>
            (await service.GetSummaryAsync(user.GetUserId(), id, currency, mode, ct)).ToOk());

        return app;
    }
}
