using System.Security.Claims;
using ExpensesBot.Api.Application;
using ExpensesBot.Api.Authentication;
using ExpensesBot.Api.Common;
using ExpensesBot.Api.Contracts.Categories;

namespace ExpensesBot.Api.Endpoints;

public static class CategoriesEndpoints
{
    public static IEndpointRouteBuilder MapCategoriesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/categories")
            .WithTags("Categories")
            .RequireAuthorization();

        group.MapGet("/", (ClaimsPrincipal user, CategoryService service, CancellationToken ct) =>
            service.GetAllAsync(user.GetUserId(), ct));

        group.MapPost("/", async (CreateCategoryRequest request, ClaimsPrincipal user, CategoryService service, CancellationToken ct) =>
            (await service.CreateAsync(user.GetUserId(), request, ct)).ToCreated(c => $"/api/categories/{c.Id}"));

        group.MapPut("/{id:long}", async (long id, UpdateCategoryRequest request, ClaimsPrincipal user, CategoryService service, CancellationToken ct) =>
            (await service.UpdateAsync(user.GetUserId(), id, request, ct)).ToOk());

        group.MapDelete("/{id:long}", async (long id, ClaimsPrincipal user, CategoryService service, CancellationToken ct) =>
            (await service.DeleteAsync(user.GetUserId(), id, ct)).ToNoContent());

        group.MapPost("/{id:long}/archive", async (long id, ClaimsPrincipal user, CategoryService service, CancellationToken ct) =>
            (await service.ToggleArchiveAsync(user.GetUserId(), id, ct)).ToOk());

        return app;
    }
}
