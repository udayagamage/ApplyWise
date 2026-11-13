using Polly.CircuitBreaker;

namespace ApplyWise.Infrastructure.Persistence.Resilience;

/// <summary>
/// Read-only view of the database circuit breaker's current state, for health checks,
/// diagnostics endpoints and tests.
/// </summary>
/// <remarks>
/// Registered as a singleton. The breaker must be shared by every <c>DbContext</c> instance in the
/// process; a per-context breaker would never see enough failures to open. A Polly
/// <see cref="CircuitBreakerStateProvider"/> can be attached to only one breaker, so this type
/// must not be shared between pipelines.
/// </remarks>
public sealed class DatabaseCircuitBreakerState
{
    /// <summary>Polly hook that is attached to the breaker when the pipeline is built.</summary>
    internal CircuitBreakerStateProvider Provider { get; } = new();

    /// <summary>
    /// Current circuit state. Polly reports <see cref="CircuitState.Closed"/> until the provider is
    /// attached, i.e. before the pipeline is first built (no database call made yet), which is
    /// the correct initial state.
    /// </summary>
    public CircuitState State => Provider.CircuitState;
}
