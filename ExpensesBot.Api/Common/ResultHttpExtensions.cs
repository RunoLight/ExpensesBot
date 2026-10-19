namespace ExpensesBot.Api.Common;

/// <summary>Maps service results to HTTP responses (RFC 9457 problem details for errors).</summary>
public static class ResultHttpExtensions
{
    public static IResult ToOk<T>(this Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error!.ToProblem();

    public static IResult ToCreated<T>(this Result<T> result, Func<T, string> location) =>
        result.IsSuccess ? TypedResults.Created(location(result.Value!), result.Value) : result.Error!.ToProblem();

    public static IResult ToNoContent(this Result result) =>
        result.IsSuccess ? TypedResults.NoContent() : result.Error!.ToProblem();

    public static IResult ToProblem(this Error error)
    {
        var (status, title) = error.Kind switch
        {
            ErrorKind.NotFound => (StatusCodes.Status404NotFound, "Not found"),
            ErrorKind.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
            ErrorKind.Forbidden => (StatusCodes.Status403Forbidden, "Forbidden"),
            ErrorKind.Unprocessable => (StatusCodes.Status422UnprocessableEntity, "Unprocessable request"),
            _ => (StatusCodes.Status400BadRequest, "Invalid request")
        };

        return TypedResults.Problem(detail: error.Message, statusCode: status, title: title);
    }
}
