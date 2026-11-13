using System.Diagnostics.CodeAnalysis;
using ApplyWise.Application.Common.Exceptions;
using ApplyWise.Infrastructure.Persistence.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace ApplyWise.Infrastructure.Persistence.Resilience;

/// <summary>
/// EF Core execution strategy that adds a circuit breaker and an overall timeout (a Polly pipeline)
/// around EF Core's own SQL Server retry loop.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why derive from <see cref="SqlServerRetryingExecutionStrategy"/>?</b> It already knows which
/// SQL Server errors are transient, and its base class handles the hard parts of retrying correctly:
/// it rejects user-started transactions outside the strategy, verifies whether a commit actually
/// succeeded after a connection drop, and runs nested calls only once. Reimplementing that in a
/// Polly retry would be fragile, so Polly adds only what EF lacks.
/// </para>
/// <para>
/// <b>Transactions:</b> because this strategy retries, a transaction you start yourself must run
/// inside the strategy, so the whole unit can be replayed:
/// <code>
/// var strategy = db.Database.CreateExecutionStrategy();
/// await strategy.ExecuteAsync(async ct =>
/// {
///     await using var tx = await db.Database.BeginTransactionAsync(ct);
///     // ... work ...
///     await db.SaveChangesAsync(ct);
///     await tx.CommitAsync(ct);
/// }, cancellationToken);
/// </code>
/// </para>
/// <para>
/// <b>Errors:</b> infrastructure failures (circuit open, timeout, retries exhausted) come out as
/// <see cref="DatabaseUnavailableException"/>. Every other exception passes through unchanged.
/// </para>
/// </remarks>
public sealed class ResilientSqlExecutionStrategy : SqlServerRetryingExecutionStrategy
{
    private readonly ResiliencePipeline _pipeline;

    /// <summary>Creates the strategy. EF Core calls this once per <see cref="DbContext"/> instance.</summary>
    /// <param name="dependencies">EF Core services for the current context.</param>
    /// <param name="pipeline">The shared, process-wide database pipeline (breaker and timeout).</param>
    /// <param name="retryOptions">Retry settings for EF's transient-error loop.</param>
    public ResilientSqlExecutionStrategy(
        ExecutionStrategyDependencies dependencies,
        ResiliencePipeline pipeline,
        DatabaseRetryOptions retryOptions)
        : base(
            dependencies,
            (retryOptions ?? throw new ArgumentNullException(nameof(retryOptions))).MaxRetryCount,
            retryOptions.MaxRetryDelay,
            retryOptions.EffectiveAdditionalErrorNumbers)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    /// <inheritdoc />
    public override TResult Execute<TState, TResult>(
        TState state,
        Func<DbContext, TState, TResult> operation,
        Func<DbContext, TState, ExecutionResult<TResult>>? verifySucceeded)
    {
        // A nested call runs inside an outer strategy that already passed the breaker. Going
        // through the pipeline again would count one logical operation twice.
        if (Current is not null)
        {
            return base.Execute(state, operation, verifySucceeded);
        }

        try
        {
            // Sync path: the timeout can only take effect between attempts, because EF's sync API
            // does not accept a CancellationToken. Use async APIs in request paths.
            return _pipeline.Execute(() => base.Execute(state, operation, verifySucceeded));
        }
        catch (Exception ex) when (TryTranslate(ex, out var unavailable))
        {
            throw unavailable;
        }
    }

    /// <inheritdoc />
    public override async Task<TResult> ExecuteAsync<TState, TResult>(
        TState state,
        Func<DbContext, TState, CancellationToken, Task<TResult>> operation,
        Func<DbContext, TState, CancellationToken, Task<ExecutionResult<TResult>>>? verifySucceeded,
        CancellationToken cancellationToken = default)
    {
        if (Current is not null)
        {
            return await base.ExecuteAsync(state, operation, verifySucceeded, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            // Polly hands us a token linked to both the caller's token and the timeout, so a timeout
            // cancels the in-flight SQL command and any pending retry delay.
            return await _pipeline.ExecuteAsync(
                async token => await base.ExecuteAsync(state, operation, verifySucceeded, token).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (TryTranslate(ex, out var unavailable))
        {
            throw unavailable;
        }
    }

    /// <summary>
    /// Maps resilience-layer exceptions to <see cref="DatabaseUnavailableException"/>. The original
    /// exception becomes the inner exception. Returns <see langword="false"/> for every other
    /// exception so it propagates untouched.
    /// </summary>
    private static bool TryTranslate(Exception exception, [NotNullWhen(true)] out DatabaseUnavailableException? translated)
    {
        translated = exception switch
        {
            // Covers IsolatedCircuitException too (manual isolation), which derives from BrokenCircuitException.
            BrokenCircuitException broken =>
                new DatabaseUnavailableException(DatabaseUnavailableReason.CircuitOpen, broken.RetryAfter, broken),
            TimeoutRejectedException timeout =>
                new DatabaseUnavailableException(DatabaseUnavailableReason.Timeout, retryAfter: null, timeout),
            RetryLimitExceededException exhausted =>
                new DatabaseUnavailableException(DatabaseUnavailableReason.RetriesExhausted, retryAfter: null, exhausted),
            _ => null,
        };

        return translated is not null;
    }
}
