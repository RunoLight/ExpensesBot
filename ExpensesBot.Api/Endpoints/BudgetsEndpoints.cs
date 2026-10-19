using System.Security.Claims;
using ExpensesBot.Api.Application;
using ExpensesBot.Api.Authentication;
using ExpensesBot.Api.Common;
using ExpensesBot.Api.Contracts.Budgets;

namespace ExpensesBot.Api.Endpoints;

public static class BudgetsEndpoints
{
    public static IEndpointRouteBuilder MapBudgetsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/budgets")
            .WithTags("Budgets")
            .RequireAuthorization();

        group.MapGet("/", (ClaimsPrincipal user, BudgetService service, CancellationToken ct) =>
            service.GetActiveAsync(user.GetUserId(), ct));

        group.MapPost("/", async (CreateBudgetRequest request, ClaimsPrincipal user, BudgetService service, CancellationToken ct) =>
            (await service.CreateAsync(user.GetUserId(), request, ct)).ToCreated(b => $"/api/budgets/{b.Id}"));

        group.MapPut("/{id:long}", async (long id, UpdateBudgetRequest request, ClaimsPrincipal user, BudgetService service, CancellationToken ct) =>
            (await service.UpdateAsync(user.GetUserId(), id, request, ct)).ToOk());

        group.MapDelete("/{id:long}", async (long id, ClaimsPrincipal user, BudgetService service, CancellationToken ct) =>
            (await service.DeleteAsync(user.GetUserId(), id, ct)).ToNoContent());

        return app;
    }
}
