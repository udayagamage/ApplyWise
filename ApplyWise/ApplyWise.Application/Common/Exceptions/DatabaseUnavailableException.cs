namespace ApplyWise.Application.Common.Exceptions;

/// <summary>
/// Thrown when the database cannot serve a request because of an infrastructure problem
/// (outage, throttling, timeouts), as opposed to a problem with the request itself.
/// </summary>
/// <remarks>
/// <para>
/// Infrastructure raises this single exception for every "database is down or unhealthy"
/// case, so the outer layers (API, Worker, Functions) handle it once instead of reacting to
/// Polly, EF Core or SqlClient exception types. The original exception is kept in
/// <see cref="Exception.InnerException"/> for diagnostics.
/// </para>
/// <para>
/// Hosts should map this to a "try again later" response, such as HTTP 503 with a
/// <c>Retry-After</c> header. It must never be shown to users as a validation or business error.
/// </para>
/// </remarks>
public sealed class DatabaseUnavailableException : Exception
{
    /// <summary>Creates a new <see cref="DatabaseUnavailableException"/>.</summary>
    /// <param name="reason">Why the database was considered unavailable.</param>
    /// <param name="retryAfter">Suggested wait before retrying, when known (e.g. the remaining circuit break time).</param>
    /// <param name="innerException">The underlying infrastructure exception.</param>
    public DatabaseUnavailableException(
        DatabaseUnavailableReason reason,
        TimeSpan? retryAfter = null,
        Exception? innerException = null)
        : base(BuildMessage(reason), innerException)
    {
        Reason = reason;
        RetryAfter = retryAfter;
    }

    /// <summary>Why the database was considered unavailable.</summary>
    public DatabaseUnavailableReason Reason { get; }

    /// <summary>
    /// Suggested wait before the caller retries, or <see langword="null"/> when unknown.
    /// Set when the circuit is open, from the time remaining until it half-opens.
    /// </summary>
    public TimeSpan? RetryAfter { get; }

    private static string BuildMessage(DatabaseUnavailableReason reason) => reason switch
    {
        DatabaseUnavailableReason.CircuitOpen =>
            "The database circuit breaker is open; the request was rejected without contacting the database.",
        DatabaseUnavailableReason.Timeout =>
            "The database operation exceeded its overall time budget.",
        DatabaseUnavailableReason.RetriesExhausted =>
            "The database operation failed after exhausting all retries for transient errors.",
        _ => "The database is temporarily unavailable.",
    };
}
