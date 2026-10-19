using System.Security.Claims;
using ExpensesBot.Api.Application;
using ExpensesBot.Api.Authentication;
using ExpensesBot.Api.Common;
using ExpensesBot.Api.Contracts.Tags;

namespace ExpensesBot.Api.Endpoints;

public static class TagEndpoints
{
    public static IEndpointRouteBuilder MapTagEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/tags")
            .WithTags("Tags")
            .RequireAuthorization();

        group.MapGet("/", (ClaimsPrincipal user, TagService service, CancellationToken ct) =>
            service.GetAllAsync(user.GetUserId(), ct));

        group.MapPost("/", async (CreateTagRequest request, ClaimsPrincipal user, TagService service, CancellationToken ct) =>
            (await service.CreateAsync(user.GetUserId(), request, ct)).ToCreated(t => $"/api/v1/tags/{t.Id}"));

        group.MapPut("/{id:long}", async (long id, UpdateTagRequest request, ClaimsPrincipal user, TagService service, CancellationToken ct) =>
            (await service.UpdateAsync(user.GetUserId(), id, request, ct)).ToOk());

        group.MapDelete("/{id:long}", async (long id, ClaimsPrincipal user, TagService service, CancellationToken ct) =>
            (await service.DeleteAsync(user.GetUserId(), id, ct)).ToNoContent());

        return app;
    }
}
