using System.Security.Claims;
using ExpensesBot.Api.Application;
using ExpensesBot.Api.Authentication;
using ExpensesBot.Api.Common;
using ExpensesBot.Api.Contracts.Goals;

namespace ExpensesBot.Api.Endpoints;

public static class GoalsEndpoints
{
    public static IEndpointRouteBuilder MapGoalsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/goals")
            .WithTags("Goals")
            .RequireAuthorization();

        group.MapGet("/", (ClaimsPrincipal user, GoalService service, CancellationToken ct) =>
            service.GetAllAsync(user.GetUserId(), ct));

        group.MapPost("/", async (CreateGoalRequest request, ClaimsPrincipal user, GoalService service, CancellationToken ct) =>
            (await service.CreateAsync(user.GetUserId(), request, ct)).ToCreated(g => $"/api/v1/goals/{g.Id}"));

        group.MapPut("/{id:long}", async (long id, UpdateGoalRequest request, ClaimsPrincipal user, GoalService service, CancellationToken ct) =>
            (await service.UpdateAsync(user.GetUserId(), id, request, ct)).ToOk());

        group.MapDelete("/{id:long}", async (long id, ClaimsPrincipal user, GoalService service, CancellationToken ct) =>
            (await service.DeleteAsync(user.GetUserId(), id, ct)).ToNoContent());

        group.MapPost("/{id:long}/contribute", async (long id, ContributeToGoalRequest request, ClaimsPrincipal user, GoalService service, CancellationToken ct) =>
            (await service.ContributeAsync(user.GetUserId(), id, request, ct)).ToOk());

        return app;
    }
}
