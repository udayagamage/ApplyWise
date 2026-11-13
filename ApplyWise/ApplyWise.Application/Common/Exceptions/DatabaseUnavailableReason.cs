namespace ApplyWise.Application.Common.Exceptions;

/// <summary>
/// Why the database was considered unavailable. Lets callers and telemetry tell
/// "we are protecting the database" (circuit open) apart from "the database failed us".
/// </summary>
public enum DatabaseUnavailableReason
{
    /// <summary>
    /// The circuit breaker is open: the call was rejected immediately and never reached SQL Server.
    /// </summary>
    CircuitOpen,

    /// <summary>The whole operation, retries included, exceeded its time budget.</summary>
    Timeout,

    /// <summary>Every retry attempt failed with a transient error.</summary>
    RetriesExhausted,
}
