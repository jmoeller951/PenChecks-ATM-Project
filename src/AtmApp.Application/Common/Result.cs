namespace AtmApp.Application.Common;

/// <summary>
/// Carries the outcome of an expected business failure (insufficient funds, invalid amount)
/// without throwing. Exceptions are reserved for truly exceptional cases — see §3 of the design doc.
/// </summary>
public class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }
    public ErrorType? ErrorType { get; }

    private Result(bool isSuccess, T? value, string? error, ErrorType? errorType)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
        ErrorType = errorType;
    }

    public static Result<T> Success(T value) => new(true, value, null, null);

    public static Result<T> Failure(string error, ErrorType errorType) =>
        new(false, default, error, errorType);
}
