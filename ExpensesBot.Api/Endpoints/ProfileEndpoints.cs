using System.Security.Claims;
using ExpensesBot.Api.Application;
using ExpensesBot.Api.Authentication;
using ExpensesBot.Api.Common;
using ExpensesBot.Api.Contracts.Profile;

namespace ExpensesBot.Api.Endpoints;

public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/profile")
            .WithTags("Profile")
            .RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, ProfileService service, CancellationToken ct) =>
            (await service.GetAsync(user.GetUserId(), ct)).ToOk());

        group.MapPut("/currency", async (SetDefaultCurrencyRequest request, ClaimsPrincipal user, ProfileService service, CancellationToken ct) =>
        {
            var result = await service.SetDefaultCurrencyAsync(user.GetUserId(), request, ct);
            return result.IsSuccess
                ? Results.Ok(new { DefaultCurrency = result.Value })
                : result.Error!.ToProblem();
        });

        group.MapDelete("/", async (ClaimsPrincipal user, ProfileService service, CancellationToken ct) =>
            (await service.DeleteAccountAsync(user.GetUserId(), ct)).ToNoContent());

        return app;
    }
}
