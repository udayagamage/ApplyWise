namespace ApplyWise.Infrastructure.Persistence.Options;

/// <summary>
/// Strongly-typed settings for the relational database, bound from the
/// <c>"Database"</c> configuration section and validated at startup by
/// <see cref="DatabaseOptionsValidator"/>.
/// </summary>
/// <remarks>
/// The connection string itself is deliberately NOT part of this section. It lives under
/// <c>ConnectionStrings:ApplyWiseDb</c> so that it works with the standard .NET conventions
/// (user secrets, Azure App Service connection strings, Key Vault, Aspire resource injection).
/// </remarks>
public sealed class DatabaseOptions
{
    /// <summary>Configuration section these options are bound from.</summary>
    public const string SectionName = "Database";

    /// <summary>Name of the connection string entry under <c>ConnectionStrings</c>.</summary>
    public const string ConnectionStringName = "ApplyWiseDb";

    /// <summary>
    /// Timeout for a single SQL command, in seconds. This is per attempt; the overall
    /// budget across retries is <see cref="DatabaseTimeoutOptions.OperationTimeout"/>.
    /// </summary>
    public int CommandTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Includes parameter values in EF Core logs and exceptions.
    /// Only honoured in the Development environment, because it can leak PII and secrets.
    /// </summary>
    public bool EnableSensitiveDataLogging { get; init; }

    /// <summary>
    /// Adds richer, slower diagnostics to query exceptions.
    /// Only honoured in the Development environment.
    /// </summary>
    public bool EnableDetailedErrors { get; init; }

    /// <summary>Retry, circuit-breaker and timeout settings (<c>Database:Resilience</c>).</summary>
    public DatabaseResilienceOptions Resilience { get; init; } = new();
}
