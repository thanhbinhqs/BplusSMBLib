using SmbEnterprise.Core.Results;
using Microsoft.Extensions.Logging;

namespace SmbEnterprise.Protocol.SMB.Retry;

/// <summary>
/// Configurable retry engine with exponential backoff, handling
/// SMB-specific transient errors (STATUS_PENDING, CONNECTION_RESET, etc.).
/// </summary>
public sealed class SmbRetryEngine
{
    private readonly ILogger _logger;
    private readonly RetryPolicy _policy;

    public SmbRetryEngine(ILogger logger, RetryPolicy? policy = null)
    {
        _logger = logger;
        _policy = policy ?? RetryPolicy.Default;
    }

    /// <summary>Execute an async operation with automatic retry on retryable errors.</summary>
    public async Task<Result<T>> ExecuteAsync<T>(
        Func<CancellationToken, Task<Result<T>>> operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        var attempt = 0;

        while (true)
        {
            attempt++;

            try
            {
                var result = await operation(cancellationToken).ConfigureAwait(false);

                if (result.IsSuccess)
                    return result;

                if (!result.Error.IsRetryable || attempt >= _policy.MaxAttempts)
                {
                    if (attempt > 1)
                        _logger.LogError("Operation {Op} failed after {Attempts} attempts: {Error}",
                            operationName, attempt, result.Error);
                    return result;
                }

                var delay = CalculateDelay(attempt);
                _logger.LogWarning(
                    "Operation {Op} attempt {Attempt}/{Max} failed with retryable error {Error}. Retrying in {Delay}ms",
                    operationName, attempt, _policy.MaxAttempts, result.Error.Code, delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (attempt >= _policy.MaxAttempts)
                {
                    _logger.LogError(ex, "Operation {Op} failed with unhandled exception after {Attempts} attempts",
                        operationName, attempt);
                    return Result<T>.Fail(ErrorCode.Unknown, ex.Message);
                }

                var delay = CalculateDelay(attempt);
                _logger.LogWarning(ex,
                    "Operation {Op} attempt {Attempt}/{Max} threw exception. Retrying in {Delay}ms",
                    operationName, attempt, _policy.MaxAttempts, delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Execute a void operation with retry.</summary>
    public async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                await operation(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < _policy.MaxAttempts)
            {
                var delay = CalculateDelay(attempt);
                _logger.LogWarning(ex,
                    "Operation {Op} attempt {Attempt}/{Max} threw exception. Retrying in {Delay}ms",
                    operationName, attempt, _policy.MaxAttempts, delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private TimeSpan CalculateDelay(int attempt)
    {
        // Exponential backoff with jitter: base * 2^(attempt-1) + random jitter
        var baseMs = _policy.BaseDelayMs * Math.Pow(2, attempt - 1);
        var cappedMs = Math.Min(baseMs, _policy.MaxDelayMs);
        var jitter = Random.Shared.Next(0, _policy.JitterMs);
        return TimeSpan.FromMilliseconds(cappedMs + jitter);
    }
}

public sealed class RetryPolicy
{
    public int MaxAttempts { get; init; } = 5;
    public int BaseDelayMs { get; init; } = 200;
    public int MaxDelayMs { get; init; } = 30_000;
    public int JitterMs { get; init; } = 100;

    public static RetryPolicy Default { get; } = new();

    public static RetryPolicy Aggressive { get; } = new()
    {
        MaxAttempts = 10,
        BaseDelayMs = 100,
        MaxDelayMs = 60_000,
        JitterMs = 200
    };
}
