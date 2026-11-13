using ApplyWise.Infrastructure.Persistence.Options;

namespace ApplyWise.UnitTests.Infrastructure.Persistence.Options;

public sealed class DatabaseOptionsValidatorTests
{
    private readonly DatabaseOptionsValidator _validator = new();

    [Fact]
    public void Defaults_AreValid()
    {
        var result = _validator.Validate(name: null, new DatabaseOptions());

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Fact]
    public void InvalidValues_AreAllReported_WithConfigurationKeys()
    {
        var options = new DatabaseOptions
        {
            CommandTimeoutSeconds = 0,
            Resilience = new DatabaseResilienceOptions
            {
                Retry = new DatabaseRetryOptions { MaxRetryCount = -1, MaxRetryDelay = TimeSpan.Zero },
                CircuitBreaker = new DatabaseCircuitBreakerOptions
                {
                    FailureRatio = 1.5,
                    MinimumThroughput = 1,
                    SamplingDuration = TimeSpan.FromMilliseconds(100),
                    BreakDuration = TimeSpan.FromMilliseconds(100),
                },
                Timeout = new DatabaseTimeoutOptions { OperationTimeout = TimeSpan.FromMilliseconds(1) },
            },
        };

        var result = _validator.Validate(name: null, options);

        Assert.True(result.Failed);
        var failures = result.Failures!.ToList();
        Assert.Equal(8, failures.Count);
        Assert.Contains(failures, f => f.StartsWith("Database:CommandTimeoutSeconds", StringComparison.Ordinal));
        Assert.Contains(failures, f => f.StartsWith("Database:Resilience:Retry:MaxRetryCount", StringComparison.Ordinal));
        Assert.Contains(failures, f => f.StartsWith("Database:Resilience:CircuitBreaker:FailureRatio", StringComparison.Ordinal));
        Assert.Contains(failures, f => f.StartsWith("Database:Resilience:Timeout:OperationTimeout", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.1)]
    [InlineData(1.01)]
    public void FailureRatio_OutOfRange_IsRejected(double ratio)
    {
        var options = new DatabaseOptions
        {
            Resilience = new DatabaseResilienceOptions
            {
                CircuitBreaker = new DatabaseCircuitBreakerOptions { FailureRatio = ratio },
            },
        };

        Assert.True(_validator.Validate(name: null, options).Failed);
    }

    [Fact]
    public void ZeroRetries_IsAllowed()
    {
        var options = new DatabaseOptions
        {
            Resilience = new DatabaseResilienceOptions { Retry = new DatabaseRetryOptions { MaxRetryCount = 0 } },
        };

        Assert.True(_validator.Validate(name: null, options).Succeeded);
    }
}
