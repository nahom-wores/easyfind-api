namespace EasyFind.Api.Models.Dto.Common;

public enum ErrorType
{
    None = 0,
    Validation = 1, // bad input        → 400
    NotFound = 2, // doesn't exist    → 404
    Conflict = 3, // already exists   → 409
    Forbidden = 4, // not allowed      → 403
    Unauthorized = 5, // not authenticated→ 401
    Failure = 6, // generic server   → 500
}

public class Result
{
    public bool IsSuccess { get; protected init; }
    public bool IsFailure => !IsSuccess;
    public string? Error { get; protected init; }
    public ErrorType ErrorType { get; protected init; } = ErrorType.None;

    protected Result(bool isSuccess, string? error, ErrorType errorType)
    {
        IsSuccess = isSuccess;
        Error = error;
        ErrorType = errorType;
    }
    // static factory methods
    public static Result Success() => new(true, null, ErrorType.None);
    public static Result Failure(string error, ErrorType type) => new(false, error, type);

    // Convenience factories for the common cases
    public static Result NotFound(string error) => new(false, error, ErrorType.NotFound);
    public static Result Conflict(string error) => new(false, error, ErrorType.Conflict);
    public static Result Validation(string error) => new(false, error, ErrorType.Validation);
    public static Result Forbidden(string error) => new(false, error, ErrorType.Forbidden);

    // Let a typed result collapse into a non-typed one
    public static Result<T> Success<T>(T value) => Result<T>.Success(value);
}

public class Result<T> : Result
{
    public T? Value { get; private init; }

    private Result(bool isSuccess, T? value, string? error, ErrorType errorType)
        : base(isSuccess, error, errorType)
    {
        Value = value;
    }

    public static Result<T> Success(T value) => new(true, value, null, ErrorType.None);
    public static new Result<T> Failure(string error, ErrorType type) => new(false, default, error, type);

    public static new Result<T> NotFound(string error) => new(false, default, error, ErrorType.NotFound);
    public static new Result<T> Conflict(string error) => new(false, default, error, ErrorType.Conflict);
    public static new Result<T> Validation(string error) => new(false, default, error, ErrorType.Validation);
    public static new Result<T> Forbidden(string error) => new(false, default, error, ErrorType.Forbidden);
}