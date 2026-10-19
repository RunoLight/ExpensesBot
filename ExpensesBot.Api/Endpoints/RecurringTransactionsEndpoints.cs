using System.Security.Claims;
using ExpensesBot.Api.Application;
using ExpensesBot.Api.Authentication;
using ExpensesBot.Api.Common;
using ExpensesBot.Api.Contracts.RecurringTransactions;

namespace ExpensesBot.Api.Endpoints;

public static class RecurringTransactionsEndpoints
{
    public static IEndpointRouteBuilder MapRecurringTransactionsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/recurring")
            .WithTags("RecurringTransactions")
            .RequireAuthorization();

        group.MapGet("/", (ClaimsPrincipal user, RecurringTransactionService service, CancellationToken ct) =>
            service.GetAllAsync(user.GetUserId(), ct));

        group.MapPost("/", async (CreateRecurringTransactionRequest request, ClaimsPrincipal user, RecurringTransactionService service, CancellationToken ct) =>
            (await service.CreateAsync(user.GetUserId(), request, ct)).ToCreated(r => $"/api/v1/recurring/{r.Id}"));

        group.MapPut("/{id:long}", async (long id, UpdateRecurringTransactionRequest request, ClaimsPrincipal user, RecurringTransactionService service, CancellationToken ct) =>
            (await service.UpdateAsync(user.GetUserId(), id, request, ct)).ToOk());

        group.MapDelete("/{id:long}", async (long id, ClaimsPrincipal user, RecurringTransactionService service, CancellationToken ct) =>
            (await service.DeleteAsync(user.GetUserId(), id, ct)).ToNoContent());

        group.MapPost("/{id:long}/execute", async (long id, ClaimsPrincipal user, RecurringTransactionService service, CancellationToken ct) =>
            (await service.ExecuteNowAsync(user.GetUserId(), id, ct)).ToOk());

        return app;
    }
}
