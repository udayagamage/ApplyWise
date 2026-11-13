using ApplyWise.Application.Common.Interfaces;
using ApplyWise.Infrastructure.Persistence.Context;
using ApplyWise.Infrastructure.Persistence.Options;
using ApplyWise.Infrastructure.Persistence.Resilience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;

namespace ApplyWise.Infrastructure;

/// <summary>
/// Composition root for the Infrastructure layer. Hosts (API, Worker, Functions) call
/// <see cref="AddInfrastructure"/> once at startup.
/// </summary>
public static class DependencyInjection
{
    /// <summary>Health-check tag for checks that decide whether the instance can take traffic.</summary>
    public const string ReadyHealthCheckTag = "ready";

    /// <summary>
    /// Registers persistence (EF Core on SQL Server with retry, circuit breaker and timeout),
    /// its health checks, and other infrastructure services.
    /// </summary>
    /// <param name="services">The application's service collection.</param>
    /// <param name="configuration">Configuration that holds the connection string and the <c>Database</c> section.</param>
    /// <param name="environment">Host environment. Diagnostic switches only take effect in Development.</param>
    /// <returns>The same <paramref name="services"/> instance, so calls can be chained.</returns>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddDatabaseOptions(configuration);
        services.AddDatabaseResilience();
        services.AddPersistence(configuration, environment);
        services.AddDatabaseHealthChecks();

        return services;
    }

    private static void AddDatabaseOptions(this IServiceCollection services, IConfiguration configuration)
    {
        // ValidateOnStart makes bad configuration fail at boot instead of on the first request.
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();
    }

    private static void AddDatabaseResilience(this IServiceCollection services)
    {
        // Process-wide circuit state. One breaker for all DbContext instances.
        services.AddSingleton<DatabaseCircuitBreakerState>();

        // Polly.Extensions builds the pipeline lazily once, caches it as a singleton, and adds
        // built-in resilience telemetry (metrics and logs) that OpenTelemetry picks up.
        services.AddResiliencePipeline(DatabaseResiliencePipeline.Name, (builder, context) =>
        {
            var sp = context.ServiceProvider;
            DatabaseResiliencePipeline.Configure(
                builder,
                sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.Resilience,
                sp.GetRequiredService<DatabaseCircuitBreakerState>(),
                sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseResiliencePipeline).FullName!));
        });
    }

    private static void AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // Read the connection string eagerly so a missing value stops startup with a clear message.
        var connectionString = configuration.GetConnectionString(DatabaseOptions.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{DatabaseOptions.ConnectionStringName}' is not configured. " +
                $"Set 'ConnectionStrings:{DatabaseOptions.ConnectionStringName}' via appsettings, user secrets or environment variables.");
        }

        // Use the (serviceProvider, options) overload so the validated options and the shared pipeline come from DI.
        services.AddDbContext<ApplyWiseDbContext>((sp, options) =>
        {
            var db = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            var pipeline = sp.GetRequiredService<ResiliencePipelineProvider<string>>()
                .GetPipeline(DatabaseResiliencePipeline.Name);

            options.UseSqlServer(connectionString, sql =>
            {
                // Migrations live with the DbContext (Infrastructure), not with the startup project.
                sql.MigrationsAssembly(typeof(ApplyWiseDbContext).Assembly.FullName);
                sql.CommandTimeout(db.CommandTimeoutSeconds);

                // Replaces EnableRetryOnFailure(). This strategy does the same retries AND adds the
                // breaker and timeout. Using both would nest the retry loops.
                sql.ExecutionStrategy(dependencies =>
                    new ResilientSqlExecutionStrategy(dependencies, pipeline, db.Resilience.Retry));
            });

            // Never allow these in non-Development environments, even if configuration asks for them:
            // they can leak sensitive data into logs.
            if (environment.IsDevelopment())
            {
                options.EnableSensitiveDataLogging(db.EnableSensitiveDataLogging);
                options.EnableDetailedErrors(db.EnableDetailedErrors);
            }
        });

        // Expose the same scoped instance through the Application abstraction so that
        // handlers using IApplicationDbContext share one change tracker and unit of work per request.
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplyWiseDbContext>());
    }

    private static void AddDatabaseHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            // Real connectivity probe (CanConnectAsync). It runs through the execution strategy, so
            // while the circuit is open it fails fast instead of hammering a struggling database,
            // and probes during half-open help the circuit close again.
            .AddDbContextCheck<ApplyWiseDbContext>(
                name: "database",
                tags: [ReadyHealthCheckTag])
            // Cheap in-memory check: reports Degraded while the circuit is not closed.
            .AddCheck<DatabaseCircuitBreakerHealthCheck>(
                name: "database-circuit-breaker",
                failureStatus: HealthStatus.Degraded,
                tags: [ReadyHealthCheckTag]);
    }
}
