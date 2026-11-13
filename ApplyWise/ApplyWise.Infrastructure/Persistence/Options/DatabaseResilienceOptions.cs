namespace ApplyWise.Infrastructure.Persistence.Options;

/// <summary>
/// Resilience settings for database access, bound from <c>Database:Resilience</c>.
/// </summary>
/// <remarks>
/// Every EF Core operation runs through these layers, outermost first:
/// <code>
/// Circuit breaker  → fail fast while the database is known to be unhealthy
///   └─ Timeout     → hard time budget for the whole operation, retries included
///        └─ Retry  → EF Core execution-strategy retry loop for transient SQL errors
///             └─ SQL command (per-attempt CommandTimeout)
/// </code>
/// The breaker is outermost so that each logical operation counts as ONE success or failure,
/// whatever the number of attempts, and so that timeouts count as failures too.
/// </remarks>
public sealed class DatabaseResilienceOptions
{
    /// <summary>Retry settings for transient SQL errors.</summary>
    public DatabaseRetryOptions Retry { get; init; } = new();

    /// <summary>Circuit-breaker settings.</summary>
    public DatabaseCircuitBreakerOptions CircuitBreaker { get; init; } = new();

    /// <summary>Settings for the overall operation timeout.</summary>
    public DatabaseTimeoutOptions Timeout { get; init; } = new();
}
