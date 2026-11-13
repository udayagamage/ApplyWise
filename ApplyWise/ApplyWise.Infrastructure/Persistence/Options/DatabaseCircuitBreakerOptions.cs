namespace ApplyWise.Infrastructure.Persistence.Options;

/// <summary>
/// Circuit-breaker settings (<c>Database:Resilience:CircuitBreaker</c>).
/// </summary>
/// <remarks>
/// The circuit opens when, within <see cref="SamplingDuration"/>, at least
/// <see cref="MinimumThroughput"/> operations ran and at least <see cref="FailureRatio"/> of them
/// failed for infrastructure reasons (retries exhausted or timed out). While open, calls fail
/// immediately for <see cref="BreakDuration"/>. After that, one trial call is let through
/// (half-open): success closes the circuit, failure opens it again.
/// <para>
/// Business errors (constraint violations, concurrency conflicts, bad SQL) never count as failures.
/// </para>
/// </remarks>
public sealed class DatabaseCircuitBreakerOptions
{
    /// <summary>Share of failed operations, from 0 (exclusive) to 1 (inclusive), that opens the circuit.</summary>
    public double FailureRatio { get; init; } = 0.5;

    /// <summary>
    /// Minimum number of operations in the sampling window before the ratio is evaluated.
    /// Stops the circuit opening on one or two unlucky calls during quiet periods. Minimum 2.
    /// </summary>
    public int MinimumThroughput { get; init; } = 10;

    /// <summary>Rolling window over which failures are measured. Minimum 500 ms.</summary>
    public TimeSpan SamplingDuration { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How long the circuit stays open before a trial call is allowed. Minimum 500 ms.</summary>
    public TimeSpan BreakDuration { get; init; } = TimeSpan.FromSeconds(30);
}
