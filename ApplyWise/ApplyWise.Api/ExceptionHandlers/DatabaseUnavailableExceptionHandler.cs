using System.Globalization;
using ApplyWise.Application.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;

namespace ApplyWise.Api.ExceptionHandlers;

/// <summary>
/// Turns <see cref="DatabaseUnavailableException"/> into an RFC 9457 <c>ProblemDetails</c> response
/// with HTTP <c>503 Service Unavailable</c> and, when known, a <c>Retry-After</c> header.
/// </summary>
/// <remarks>
/// A 503 tells clients and gateways that the failure is temporary and safe to retry, unlike a
/// generic 500. Retry-After lets well-behaved clients back off for as long as the circuit stays
/// open instead of retrying straight away. No internal details (SQL errors, server names) are
/// returned; the trace id from the default ProblemDetails customization links the response to logs.
/// </remarks>
internal sealed partial class DatabaseUnavailableExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<DatabaseUnavailableExceptionHandler> logger) : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DatabaseUnavailableException unavailable)
        {
            // Not ours: let the next handler or the default 500 pipeline deal with it.
            return false;
        }

        // Warning, not Error: this is expected, managed degradation. The breaker has already logged
        // the root cause once at Error level, which avoids an error-log storm while the circuit is open.
        LogDatabaseUnavailable(logger, unavailable.Reason, httpContext.Request.Method, httpContext.Request.Path, unavailable);

        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;

        if (unavailable.RetryAfter is { } retryAfter)
        {
            // Retry-After is in whole seconds; round up so clients never come back too early.
            var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            httpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Type = "https://tools.ietf.org/html/rfc9110#section-15.6.4",
                Title = "Service temporarily unavailable",
                Detail = "The service cannot reach its data store right now. Please retry later.",
            },
        }).ConfigureAwait(false);
    }

    [LoggerMessage(
        EventId = 5100,
        Level = LogLevel.Warning,
        Message = "Database unavailable ({Reason}) while handling {Method} {Path}; returning 503.")]
    private static partial void LogDatabaseUnavailable(
        ILogger logger,
        DatabaseUnavailableReason reason,
        string method,
        PathString path,
        Exception exception);
}
