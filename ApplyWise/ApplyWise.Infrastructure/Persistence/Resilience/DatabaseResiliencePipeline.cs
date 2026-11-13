using ApplyWise.Infrastructure.Persistence.Options;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace ApplyWise.Infrastructure.Persistence.Resilience;

/// <summary>
/// Builds the Polly pipeline (circuit breaker, then timeout) that wraps every EF Core operation.
/// Retries are handled one level down by <see cref="ResilientSqlExecutionStrategy"/>.
/// </summary>
/// <remarks>
/// This is a static configuration method rather than inline DI code so that unit tests can build
/// exactly the same pipeline with a fake <see cref="TimeProvider"/>.
/// </remarks>
public static class DatabaseResiliencePipeline
{
    /// <summary>Key of the pipeline in Polly's <c>ResiliencePipelineProvider&lt;string&gt;</c>. Also shows up in telemetry.</summary>
    public const string Name = "ApplyWise.Database";

    private static readonly Action<ILogger, TimeSpan, Exception?> LogCircuitOpened =
        LoggerMessage.Define<TimeSpan>(
            LogLevel.Error,
            new EventId(5001, "DatabaseCircuitOpened"),
            "Database circuit breaker OPENED. Database calls will fail fast for {BreakDuration}.");

    private static readonly Action<ILogger, Exception?> LogCircuitHalfOpened =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(5002, "DatabaseCircuitHalfOpened"),
            "Database circuit breaker HALF-OPEN. Allowing a trial call to probe database health.");

    private static readonly Action<ILogger, Exception?> LogCircuitClosed =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(5003, "DatabaseCircuitClosed"),
            "Database circuit breaker CLOSED. Database access restored.");

    private static readonly Action<ILogger, TimeSpan, Exception?> LogOperationTimedOut =
        LoggerMessage.Define<TimeSpan>(
            LogLevel.Warning,
            new EventId(5004, "DatabaseOperationTimedOut"),
            "Database operation exceeded its overall timeout of {Timeout} and was cancelled.");

    /// <summary>
    /// Adds the database circuit breaker and timeout strategies to <paramref name="builder"/>.
    /// </summary>
    /// <param name="builder">Builder to configure. Set its <c>TimeProvider</c> before calling, to control time in tests.</param>
    /// <param name="options">Validated resilience settings.</param>
    /// <param name="circuitState">Shared state holder; attached to the breaker so health checks can read it.</param>
    /// <param name="logger">Logger for circuit state changes and timeouts.</param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    public static ResiliencePipelineBuilder Configure(
        ResiliencePipelineBuilder builder,
        DatabaseResilienceOptions options,
        DatabaseCircuitBreakerState circuitState,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(circuitState);
        ArgumentNullException.ThrowIfNull(logger);

        var breaker = options.CircuitBreaker;

        // Order matters: the first strategy added is the outermost.
        return builder
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                Name = "DatabaseCircuitBreaker",
                FailureRatio = breaker.FailureRatio,
                MinimumThroughput = breaker.MinimumThroughput,
                SamplingDuration = breaker.SamplingDuration,
                BreakDuration = breaker.BreakDuration,
                StateProvider = circuitState.Provider,

                // Count only infrastructure failures. By the time an exception gets here, the EF
                // retry loop has already classified it:
                //  - RetryLimitExceededException: transient errors that outlasted every retry.
                //  - TimeoutRejectedException: the operation's time budget was exceeded.
                // Anything else (unique-key violations, DbUpdateConcurrencyException, invalid SQL,
                // caller cancellation) comes from the request, not database health, and must NOT open the circuit.
                ShouldHandle = new PredicateBuilder()
                    .Handle<RetryLimitExceededException>()
                    .Handle<TimeoutRejectedException>(),

                OnOpened = args =>
                {
                    LogCircuitOpened(logger, args.BreakDuration, args.Outcome.Exception);
                    return ValueTask.CompletedTask;
                },
                OnHalfOpened = _ =>
                {
                    LogCircuitHalfOpened(logger, null);
                    return ValueTask.CompletedTask;
                },
                OnClosed = _ =>
                {
                    LogCircuitClosed(logger, null);
                    return ValueTask.CompletedTask;
                },
            })
            .AddTimeout(new TimeoutStrategyOptions
            {
                Name = "DatabaseOperationTimeout",
                Timeout = options.Timeout.OperationTimeout,
                OnTimeout = args =>
                {
                    LogOperationTimedOut(logger, args.Timeout, null);
                    return ValueTask.CompletedTask;
                },
            });
    }
}
