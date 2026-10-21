using HMS.Application.Common;
using HMS.Contracts.Common;
using HMS.Domain.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HMS.Api.Middleware;

/// <summary>Maps exceptions to the stable <see cref="ApiError"/> contract. Unexpected errors never leak details.</summary>
internal sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            var (status, error) = Map(ex, context.TraceIdentifier);
            if (status >= 500)
                logger.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
            else
                logger.LogInformation("Request failed with {Status} {Code}", status, error.Code);

            context.Response.Clear();
            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(error);
        }
    }

    internal static (int Status, ApiError Error) Map(Exception ex, string traceId) => ex switch
    {
        ValidationAppException v => (400, ApiError.Create(v.Code, v.Message, traceId, v.Errors)),
        AppException a => (a.StatusCode, ApiError.Create(a.Code, a.Message, traceId)),
        DomainConflictException c => (409, ApiError.Create(c.Code, c.Message, traceId)),
        DomainException d => (400, ApiError.Create(d.Code, d.Message, traceId)),
        DbUpdateConcurrencyException => (409, ApiError.Create(ErrorCodes.ConcurrencyConflict,
            "The record was changed by someone else. Reload and try again.", traceId)),
        _ when IsDeadlock(ex) => (409, ApiError.Create(ErrorCodes.ConcurrencyConflict,
            "Another request was updating the same records. Try again.", traceId)),
        DbUpdateException when IsUniqueViolation(ex) => (409, ApiError.Create(ErrorCodes.ConcurrencyConflict,
            "The same change was just saved by another request. Reload and try again.", traceId)),
        OperationCanceledException => (499, ApiError.Create("REQUEST_CANCELLED", "The request was cancelled.", traceId)),
        _ => (500, ApiError.Create(ErrorCodes.ServerError, "An unexpected error occurred.", traceId)),
    };

    /// <summary>
    /// A unique index rejected the row. Services check for duplicates first, so this means two requests raced; the
    /// loser gets a retryable conflict instead of a 500.
    /// </summary>
    private static bool IsUniqueViolation(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is SqlException { Number: 2601 or 2627 } || e is Microsoft.Data.Sqlite.SqliteException { SqliteExtendedErrorCode: 2067 or 1555 }) return true;
        return false;
    }

    /// <summary>SQL Server deadlock victims (error 1205) are safe to retry; tell the client so instead of a 500.</summary>
    private static bool IsDeadlock(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is SqlException { Number: 1205 }) return true;
        return false;
    }
}
