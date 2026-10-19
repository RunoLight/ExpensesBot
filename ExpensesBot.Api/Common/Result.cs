namespace ExpensesBot.Api.Common;

public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    Unprocessable
}

/// <summary>An expected failure of a business operation (not an exception).</summary>
public sealed record Error(ErrorKind Kind, string Message)
{
    public static Error Validation(string message) => new(ErrorKind.Validation, message);

    public static Error NotFound(string message = "Not found.") => new(ErrorKind.NotFound, message);

    public static Error Conflict(string message) => new(ErrorKind.Conflict, message);

    /// <summary>The caller can see the resource but may not do this (e.g. a member managing a wallet).</summary>
    public static Error Forbidden(string message) => new(ErrorKind.Forbidden, message);

    /// <summary>The request is well-formed but contradicts earlier state (e.g. a reused Idempotency-Key).</summary>
    public static Error Unprocessable(string message) => new(ErrorKind.Unprocessable, message);
}

/// <summary>Outcome of an operation that returns nothing on success.</summary>
public readonly record struct Result(Error? Error)
{
    public bool IsSuccess => Error is null;

    public static Result Success() => new(null);

    public static implicit operator Result(Error error) => new(error);
}

/// <summary>Outcome of an operation: either a value or an <see cref="Common.Error"/>.</summary>
public readonly record struct Result<T>(T? Value, Error? Error)
{
    public bool IsSuccess => Error is null;

    public static implicit operator Result<T>(T value) => new(value, null);

    public static implicit operator Result<T>(Error error) => new(default, error);
}
