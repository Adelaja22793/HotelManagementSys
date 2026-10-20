using HMS.Application.Common;
using HMS.Contracts.Common;
using HMS.Domain.Common;
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
        DomainException d => (400, ApiError.Create(d.Code, d.Message, traceId)),
        DbUpdateConcurrencyException => (409, ApiError.Create(ErrorCodes.ConcurrencyConflict,
            "The record was changed by someone else. Reload and try again.", traceId)),
        OperationCanceledException => (499, ApiError.Create("REQUEST_CANCELLED", "The request was cancelled.", traceId)),
        _ => (500, ApiError.Create(ErrorCodes.ServerError, "An unexpected error occurred.", traceId)),
    };
}
