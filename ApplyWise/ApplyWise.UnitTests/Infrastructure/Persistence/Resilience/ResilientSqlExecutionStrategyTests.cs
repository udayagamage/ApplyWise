using ApplyWise.Application.Common.Exceptions;
using ApplyWise.Infrastructure.Persistence.Context;
using ApplyWise.Infrastructure.Persistence.Options;
using ApplyWise.Infrastructure.Persistence.Resilience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Polly;
using Polly.CircuitBreaker;

namespace ApplyWise.UnitTests.Infrastructure.Persistence.Resilience;

/// <summary>
/// Behaviour tests for the database resilience stack (EF retry loop + Polly breaker/timeout).
/// </summary>
/// <remarks>
/// No database is needed: the operations passed to the strategy are plain delegates that simulate
/// outcomes, and the DbContext is never opened. <see cref="TimeoutException"/> stands in for a
/// transient SQL error (EF Core's SQL Server detector treats it as transient), while
/// <see cref="InvalidOperationException"/> stands in for a non-transient/business error.
/// Time is controlled with <see cref="FakeTimeProvider"/>, so breaker and timeout tests are instant
/// and deterministic.
/// </remarks>
public sealed class ResilientSqlExecutionStrategyTests : IDisposable
{
    private static readonly TimeSpan BreakDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(5);

    private readonly FakeTimeProvider _time = new();
    private readonly DatabaseCircuitBreakerState _circuit = new();
    private ApplyWiseDbContext? _context;

    public void Dispose() => _context?.Dispose();

    [Fact]
    public async Task TransientFailure_IsRetried_AndSucceedsWhenDatabaseRecovers()
    {
        var strategy = CreateStrategy(maxRetryCount: 3);
        var attempts = 0;

        var result = await strategy.ExecuteAsync(_ =>
        {
            attempts++;
            return attempts < 3 ? throw new TimeoutException("transient") : Task.FromResult(42);
        }, CancellationToken.None);

        Assert.Equal(42, result);
        Assert.Equal(3, attempts);
        Assert.Equal(CircuitState.Closed, _circuit.State);
    }

    [Fact]
    public async Task TransientFailure_WhenRetriesExhausted_ThrowsDatabaseUnavailable_RetriesExhausted()
    {
        var strategy = CreateStrategy(maxRetryCount: 2);
        var attempts = 0;

        var ex = await Assert.ThrowsAsync<DatabaseUnavailableException>(() =>
            strategy.ExecuteAsync<int>(_ =>
            {
                attempts++;
                throw new TimeoutException("transient");
            }, CancellationToken.None));

        Assert.Equal(DatabaseUnavailableReason.RetriesExhausted, ex.Reason);
        Assert.Equal(3, attempts); // 1 initial + 2 retries
    }

    [Fact]
    public async Task NonTransientFailure_IsNotRetried_NotTranslated_AndDoesNotTripBreaker()
    {
        var strategy = CreateStrategy(maxRetryCount: 3, minimumThroughput: 2);
        var attempts = 0;

        // Far more failures than needed to open the breaker if they were (wrongly) counted.
        for (var i = 0; i < 5; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                strategy.ExecuteAsync<int>(_ =>
                {
                    attempts++;
                    throw new InvalidOperationException("e.g. unique key violation");
                }, CancellationToken.None));
        }

        Assert.Equal(5, attempts); // exactly one attempt per call: no retries
        Assert.Equal(CircuitState.Closed, _circuit.State);
    }

    [Fact]
    public async Task Breaker_OpensAfterThreshold_ThenFailsFastWithoutCallingDatabase()
    {
        var strategy = CreateStrategy(maxRetryCount: 0, minimumThroughput: 2);
        await FailTransientlyAsync(strategy, times: 2);

        Assert.Equal(CircuitState.Open, _circuit.State);

        var invoked = false;
        var ex = await Assert.ThrowsAsync<DatabaseUnavailableException>(() =>
            strategy.ExecuteAsync(_ =>
            {
                invoked = true;
                return Task.FromResult(1);
            }, CancellationToken.None));

        Assert.False(invoked, "An open circuit must not reach the database.");
        Assert.Equal(DatabaseUnavailableReason.CircuitOpen, ex.Reason);
        Assert.Equal(BreakDuration, ex.RetryAfter);
        Assert.IsType<BrokenCircuitException>(ex.InnerException);
    }

    [Fact]
    public async Task Breaker_HalfOpenTrialSucceeds_ClosesCircuit()
    {
        var strategy = CreateStrategy(maxRetryCount: 0, minimumThroughput: 2);
        await FailTransientlyAsync(strategy, times: 2);

        _time.Advance(BreakDuration);

        var result = await strategy.ExecuteAsync(_ => Task.FromResult(7), CancellationToken.None);

        Assert.Equal(7, result);
        Assert.Equal(CircuitState.Closed, _circuit.State);
    }

    [Fact]
    public async Task Breaker_HalfOpenTrialFails_ReopensCircuit()
    {
        var strategy = CreateStrategy(maxRetryCount: 0, minimumThroughput: 2);
        await FailTransientlyAsync(strategy, times: 2);

        _time.Advance(BreakDuration);
        await FailTransientlyAsync(strategy, times: 1);

        Assert.Equal(CircuitState.Open, _circuit.State);
    }

    [Fact]
    public async Task SlowOperation_ExceedingBudget_ThrowsDatabaseUnavailable_Timeout()
    {
        var strategy = CreateStrategy(maxRetryCount: 3);

        // Hangs until Polly's timeout cancels the token.
        var pending = strategy.ExecuteAsync(async ct =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return 1;
        }, CancellationToken.None);

        _time.Advance(OperationTimeout + TimeSpan.FromMilliseconds(1));

        var ex = await Assert.ThrowsAsync<DatabaseUnavailableException>(() => pending);
        Assert.Equal(DatabaseUnavailableReason.Timeout, ex.Reason);
    }

    [Fact]
    public async Task CallerCancellation_PropagatesAsCancellation_AndDoesNotTripBreaker()
    {
        var strategy = CreateStrategy(maxRetryCount: 0, minimumThroughput: 2);

        for (var i = 0; i < 3; i++)
        {
            using var cts = new CancellationTokenSource();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                strategy.ExecuteAsync<int>(ct =>
                {
                    cts.Cancel(); // client disconnects mid-operation
                    ct.ThrowIfCancellationRequested();
                    return Task.FromResult(0);
                }, cts.Token));
        }

        Assert.Equal(CircuitState.Closed, _circuit.State);
    }

    private static async Task FailTransientlyAsync(Microsoft.EntityFrameworkCore.Storage.IExecutionStrategy strategy, int times)
    {
        for (var i = 0; i < times; i++)
        {
            await Assert.ThrowsAsync<DatabaseUnavailableException>(() =>
                strategy.ExecuteAsync<int>(_ => throw new TimeoutException("transient"), CancellationToken.None));
        }
    }

    /// <summary>
    /// Builds the same pipeline production uses (via <see cref="DatabaseResiliencePipeline.Configure"/>)
    /// but on fake time, and a DbContext wired to <see cref="ResilientSqlExecutionStrategy"/>.
    /// </summary>
    private Microsoft.EntityFrameworkCore.Storage.IExecutionStrategy CreateStrategy(int maxRetryCount, int minimumThroughput = 10)
    {
        var resilience = new DatabaseResilienceOptions
        {
            // A tiny max delay keeps EF's real-time back-off negligible in tests.
            Retry = new DatabaseRetryOptions { MaxRetryCount = maxRetryCount, MaxRetryDelay = TimeSpan.FromMilliseconds(1) },
            CircuitBreaker = new DatabaseCircuitBreakerOptions
            {
                FailureRatio = 0.5,
                MinimumThroughput = minimumThroughput,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = BreakDuration,
            },
            Timeout = new DatabaseTimeoutOptions { OperationTimeout = OperationTimeout },
        };

        var pipeline = DatabaseResiliencePipeline
            .Configure(new ResiliencePipelineBuilder { TimeProvider = _time }, resilience, _circuit, NullLogger.Instance)
            .Build();

        var options = new DbContextOptionsBuilder<ApplyWiseDbContext>()
            .UseSqlServer(
                "Server=unused;Database=unused", // never opened: operations are simulated
                sql => sql.ExecutionStrategy(deps => new ResilientSqlExecutionStrategy(deps, pipeline, resilience.Retry)))
            .Options;

        _context = new ApplyWiseDbContext(options);
        return _context.Database.CreateExecutionStrategy();
    }
}
