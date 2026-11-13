namespace ApplyWise.Infrastructure.Persistence.Options;

/// <summary>
/// Settings for the overall operation timeout (<c>Database:Resilience:Timeout</c>).
/// </summary>
public sealed class DatabaseTimeoutOptions
{
    /// <summary>
    /// Hard time budget for one logical database operation, including all retries and back-off delays.
    /// Stops a slow database from holding request threads and pooled connections indefinitely.
    /// Must be between 10 ms and 24 h (Polly's limits).
    /// </summary>
    /// <remarks>
    /// This relies on cancellation, so it fully applies to async EF Core APIs only. Synchronous
    /// calls (e.g. <c>SaveChanges()</c>) can only be interrupted between attempts. Always use async in request paths.
    /// </remarks>
    public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
