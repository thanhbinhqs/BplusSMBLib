namespace SmbEnterprise.Core.Results;

/// <summary>
/// Discriminated result wrapper. Avoids exceptions for expected failure paths.
/// </summary>
public sealed class Result<T>
{
    private readonly T? _value;
    private readonly SmbError? _error;

    private Result(T value)
    {
        _value = value;
        IsSuccess = true;
    }

    private Result(SmbError error)
    {
        _error = error;
        IsSuccess = false;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"Result is failure: {_error}");
    public SmbError Error => IsFailure ? _error! : throw new InvalidOperationException("Result is success.");

    public static Result<T> Ok(T value) => new(value);
    public static Result<T> Fail(SmbError error) => new(error);
    public static Result<T> Fail(ErrorCode code, string message) => new(new SmbError(code, message));

    public Result<TOut> Map<TOut>(Func<T, TOut> mapper) =>
        IsSuccess ? Result<TOut>.Ok(mapper(_value!)) : Result<TOut>.Fail(_error!);

    public override string ToString() =>
        IsSuccess ? $"Ok({_value})" : $"Fail({_error})";
}

/// <summary>Represents an error in the SMB transfer system.</summary>
public sealed class SmbError
{
    public ErrorCode Code { get; }
    public string Message { get; }
    public Exception? InnerException { get; }
    public bool IsRetryable => Code.IsRetryable();

    public SmbError(ErrorCode code, string message, Exception? innerException = null)
    {
        Code = code;
        Message = message;
        InnerException = innerException;
    }

    public override string ToString() => $"[{Code}] {Message}";
}

public enum ErrorCode
{
    Unknown = 0,

    // Connection errors (retryable)
    ConnectionFailed = 1001,
    ConnectionReset = 1002,
    SessionExpired = 1003,
    AuthenticationFailed = 1004,
    Timeout = 1005,
    NetworkUnreachable = 1006,

    // File system errors
    FileNotFound = 2001,
    AccessDenied = 2002,
    SharingViolation = 2003,
    PathTooLong = 2004,
    InvalidPath = 2005,
    DirectoryNotEmpty = 2006,
    DiskFull = 2007,
    FileAlreadyExists = 2008,

    // Transfer errors (retryable)
    TransferInterrupted = 3001,
    ChunkCorrupted = 3002,
    ChecksumMismatch = 3003,
    ResumeOffsetInvalid = 3004,

    // Protocol errors
    ProtocolError = 4001,
    UnsupportedDialect = 4002,
    CapabilityNotSupported = 4003,

    // Job errors
    JobNotFound = 5001,
    JobAlreadyRunning = 5002,
    JobCancelled = 5003,
}

public static class ErrorCodeExtensions
{
    public static bool IsRetryable(this ErrorCode code) => code switch
    {
        ErrorCode.ConnectionFailed => true,
        ErrorCode.ConnectionReset => true,
        ErrorCode.SessionExpired => true,
        ErrorCode.Timeout => true,
        ErrorCode.NetworkUnreachable => true,
        ErrorCode.SharingViolation => true,
        ErrorCode.TransferInterrupted => true,
        ErrorCode.ChunkCorrupted => true,
        _ => false
    };
}
