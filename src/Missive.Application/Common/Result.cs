namespace Missive.Application.Common;

/// <summary>Machine-readable codes for expected failures, shared with the UI.</summary>
public static class ErrorCodes
{
    public const string NoWorkspace = "no_workspace";
    public const string NotFound = "not_found";
    public const string InvalidPath = "invalid_path";
    public const string InvalidFile = "invalid_file";
    public const string InvalidRequest = "invalid_request";
    public const string AlreadyExists = "already_exists";
    public const string FolderNotEmpty = "folder_not_empty";
    public const string Io = "io_error";
}

public sealed record Error(string Code, string Message);

/// <summary>The outcome of an operation that can fail in an expected way.</summary>
public readonly record struct Result<T>
{
    private readonly T? _value;

    private Result(T? value, Error? error)
    {
        _value = value;
        Error = error;
    }

    public Error? Error { get; }

    public bool IsSuccess => Error is null;

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"Result failed: {Error!.Message}");

    public static Result<T> Success(T value) => new(value, null);

    public static Result<T> Failure(Error error) => new(default, error);

    public static Result<T> Failure(string code, string message) => new(default, new Error(code, message));

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure(error);
}

/// <summary>A successful result that carries no value.</summary>
public readonly record struct Unit
{
    public static Unit Value { get; } = default;
}
