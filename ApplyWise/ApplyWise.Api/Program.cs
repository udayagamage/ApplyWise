using ApplyWise.Api.ExceptionHandlers;
using ApplyWise.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Aspire service defaults: OpenTelemetry (incl. Polly resilience metrics), health checks, service discovery.
builder.AddServiceDefaults();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Infrastructure: EF Core / SQL Server persistence (see ApplyWise.Infrastructure.DependencyInjection).
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

// Error handling: RFC 9457 ProblemDetails for every error response. Specific handlers run first;
// DatabaseUnavailableException -> 503 + Retry-After. Anything unhandled falls through to a generic 500.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DatabaseUnavailableExceptionHandler>();

var app = builder.Build();

// Must be first so it catches exceptions thrown by everything registered after it.
app.UseExceptionHandler();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

// /health (all checks, incl. database + circuit breaker) and /alive (liveness). Development only by default.
app.MapDefaultEndpoints();

app.Run();

internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
