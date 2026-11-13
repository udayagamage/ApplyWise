using Microsoft.Extensions.Options;

namespace ApplyWise.Infrastructure.Persistence.Options;

/// <summary>
/// Validates <see cref="DatabaseOptions"/> at startup (registered with <c>ValidateOnStart</c>).
/// </summary>
/// <remarks>
/// Reports every problem in one go, not just the first, so a misconfigured deployment
/// can be fixed in a single pass. The bounds match what Polly would reject at pipeline
/// build time; checking them here gives clear, configuration-key-based messages instead.
/// </remarks>
public sealed class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{
    private static readonly TimeSpan MinPollyDuration = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MinTimeout = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan MaxTimeout = TimeSpan.FromHours(24);

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        const string root = DatabaseOptions.SectionName;
        const string resilience = $"{root}:{nameof(DatabaseOptions.Resilience)}";
        var failures = new List<string>();

        if (options.CommandTimeoutSeconds <= 0)
        {
            failures.Add($"{root}:{nameof(DatabaseOptions.CommandTimeoutSeconds)} must be greater than 0.");
        }

        var retry = options.Resilience.Retry;
        if (retry.MaxRetryCount < 0)
        {
            failures.Add($"{resilience}:Retry:{nameof(retry.MaxRetryCount)} must be 0 or greater.");
        }

        if (retry.MaxRetryDelay <= TimeSpan.Zero)
        {
            failures.Add($"{resilience}:Retry:{nameof(retry.MaxRetryDelay)} must be a positive duration.");
        }

        var breaker = options.Resilience.CircuitBreaker;
        if (breaker.FailureRatio is <= 0 or > 1)
        {
            failures.Add($"{resilience}:CircuitBreaker:{nameof(breaker.FailureRatio)} must be greater than 0 and at most 1.");
        }

        if (breaker.MinimumThroughput < 2)
        {
            failures.Add($"{resilience}:CircuitBreaker:{nameof(breaker.MinimumThroughput)} must be at least 2.");
        }

        if (breaker.SamplingDuration < MinPollyDuration)
        {
            failures.Add($"{resilience}:CircuitBreaker:{nameof(breaker.SamplingDuration)} must be at least {MinPollyDuration}.");
        }

        if (breaker.BreakDuration < MinPollyDuration)
        {
            failures.Add($"{resilience}:CircuitBreaker:{nameof(breaker.BreakDuration)} must be at least {MinPollyDuration}.");
        }

        var timeout = options.Resilience.Timeout;
        if (timeout.OperationTimeout < MinTimeout || timeout.OperationTimeout > MaxTimeout)
        {
            failures.Add($"{resilience}:Timeout:{nameof(timeout.OperationTimeout)} must be between {MinTimeout} and {MaxTimeout}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
