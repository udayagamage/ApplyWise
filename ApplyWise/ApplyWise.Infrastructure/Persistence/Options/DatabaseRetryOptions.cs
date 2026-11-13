namespace ApplyWise.Infrastructure.Persistence.Options;

/// <summary>
/// Retry settings for transient SQL Server errors (<c>Database:Resilience:Retry</c>).
/// </summary>
/// <remarks>
/// Retries run in EF Core's execution-strategy loop rather than in a Polly retry, because only
/// EF's loop understands its own transaction semantics: it rejects user-started transactions
/// outside the strategy, verifies whether a commit actually succeeded after a connection drop,
/// and avoids nested retries. Its back-off is exponential with jitter: ~0s, ~1s, ~3s, ~7s…,
/// capped at <see cref="MaxRetryDelay"/>.
/// </remarks>
public sealed class DatabaseRetryOptions
{
    /// <summary>
    /// SQL Server error numbers for connectivity failures that are treated as transient even though
    /// EF Core's built-in list does not include them. Without these, a server that is down or
    /// unreachable (the classic circuit-breaker case) would neither be retried nor trip the breaker.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><c>-1</c>: error locating server/instance.</item>
    /// <item><c>2</c>: server not found or not accessible.</item>
    /// <item><c>53</c>: network path not found.</item>
    /// <item><c>121</c>: semaphore timeout (network stall).</item>
    /// <item><c>258</c>: "The wait operation timed out" while connecting (SqlClient reports this for an
    /// unreachable or refusing server within Connect Timeout; confirmed in the smoke test).</item>
    /// <item><c>10061</c>: connection actively refused (server not listening).</item>
    /// <item><c>11001</c>: host name could not be resolved.</item>
    /// </list>
    /// </remarks>
    public static IReadOnlyList<int> BuiltInConnectivityErrorNumbers { get; } = [-1, 2, 53, 121, 258, 10061, 11001];

    /// <summary>
    /// Maximum retry attempts per operation after the first failure. Set to 0 to disable retries;
    /// the circuit breaker and timeout still apply.
    /// </summary>
    public int MaxRetryCount { get; init; } = 3;

    /// <summary>Upper bound for the delay between two attempts.</summary>
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Extra SQL Server error numbers to treat as transient, on top of EF Core's list and
    /// <see cref="BuiltInConnectivityErrorNumbers"/>. Leave empty unless a specific error is known to be transient.
    /// </summary>
    /// <remarks>
    /// Defaults to empty on purpose: the configuration binder appends to collections instead of
    /// replacing them, so built-in values are kept in <see cref="BuiltInConnectivityErrorNumbers"/>.
    /// </remarks>
    public int[] AdditionalTransientErrorNumbers { get; init; } = [];

    /// <summary>All error numbers to add to EF Core's transient list (built-in plus configured).</summary>
    internal IEnumerable<int> EffectiveAdditionalErrorNumbers =>
        BuiltInConnectivityErrorNumbers.Concat(AdditionalTransientErrorNumbers).Distinct();
}
