using Microsoft.Extensions.Diagnostics.HealthChecks;
using Polly.CircuitBreaker;

namespace ApplyWise.Infrastructure.Persistence.Resilience;

/// <summary>
/// Reports the database circuit-breaker state as a health check.
/// </summary>
/// <remarks>
/// An open circuit is reported as <see cref="HealthStatus.Degraded"/>, not
/// <see cref="HealthStatus.Unhealthy"/>, on purpose. Every instance shares the same database, so
/// they tend to trip together. If readiness probes treated that as Unhealthy, the orchestrator
/// would pull ALL instances out of rotation at once and clients would get connection errors
/// instead of our controlled 503 + Retry-After. The instance is still alive and doing its job,
/// which is failing fast.
/// </remarks>
internal sealed class DatabaseCircuitBreakerHealthCheck(DatabaseCircuitBreakerState circuitState) : IHealthCheck
{
    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var state = circuitState.State;
        var data = new Dictionary<string, object> { ["state"] = state.ToString() };

        var result = state switch
        {
            CircuitState.Closed => HealthCheckResult.Healthy("Database circuit is closed.", data),
            CircuitState.HalfOpen => HealthCheckResult.Degraded("Database circuit is half-open; probing database health.", data: data),
            CircuitState.Open => HealthCheckResult.Degraded("Database circuit is open; database calls are failing fast.", data: data),
            CircuitState.Isolated => HealthCheckResult.Degraded("Database circuit is manually isolated.", data: data),
            _ => HealthCheckResult.Unhealthy($"Unknown circuit state '{state}'.", data: data),
        };

        return Task.FromResult(result);
    }
}
