using System.Security.Claims;
using ExpensesBot.Api.Application;
using ExpensesBot.Api.Authentication;
using ExpensesBot.Api.Common;
using ExpensesBot.Api.Contracts.Transactions;
using Microsoft.AspNetCore.Mvc;

namespace ExpensesBot.Api.Endpoints;

public static class TransactionsEndpoints
{
    public static IEndpointRouteBuilder MapTransactionsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/transactions")
            .WithTags("Transactions")
            .RequireAuthorization();

        group.MapGet("/", (ClaimsPrincipal user, TransactionService service, CancellationToken ct,
                int limit = 50, int offset = 0, long? tagId = null) =>
            service.GetPageAsync(user.GetUserId(), limit, offset, tagId, ct));

        group.MapGet("/{id:long}", async (long id, ClaimsPrincipal user, TransactionService service, CancellationToken ct) =>
            (await service.GetAsync(user.GetUserId(), id, ct)).ToOk());

        // A retried POST with the same Idempotency-Key returns the original transaction.
        group.MapPost("/", async (CreateTransactionRequest request, ClaimsPrincipal user, TransactionService service, CancellationToken ct,
                [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey) =>
            (await service.CreateAsync(user.GetUserId(), request, ct, idempotencyKey)).ToCreated(t => $"/api/transactions/{t.Id}"));

        group.MapPost("/bulk", async (List<CreateTransactionRequest>? requests, ClaimsPrincipal user, TransactionService service, CancellationToken ct) =>
            (await service.BulkCreateAsync(user.GetUserId(), requests, ct)).ToOk());

        group.MapPut("/{id:long}", async (long id, UpdateTransactionRequest request, ClaimsPrincipal user, TransactionService service, CancellationToken ct) =>
            (await service.UpdateAsync(user.GetUserId(), id, request, ct)).ToOk());

        group.MapDelete("/{id:long}", async (long id, ClaimsPrincipal user, TransactionService service, CancellationToken ct) =>
            (await service.DeleteAsync(user.GetUserId(), id, ct)).ToNoContent());

        group.MapGet("/summary", (ClaimsPrincipal user, SummaryService service, CancellationToken ct,
                string? mode = null, string? currency = null, DateTime? from = null, DateTime? to = null) =>
            service.GetAsync(user.GetUserId(), mode, currency, from, to, ct));

        group.MapGet("/export", async (ClaimsPrincipal user, TransactionService service, CancellationToken ct,
                DateTime? from = null, DateTime? to = null) =>
        {
            var csv = await service.ExportCsvAsync(user.GetUserId(), from, to, ct);
            return TypedResults.File(csv, "text/csv", $"expenses_{DateTime.UtcNow:yyyyMMdd}.csv");
        });

        return app;
    }
}
