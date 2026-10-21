using System.Net;
using HMS.Contracts.Common;

namespace HMS.Desktop.Services;

/// <summary>An API call failed. The UI shows <see cref="Message"/> and keeps the trace id for support.</summary>
public sealed class ApiException(HttpStatusCode status, ApiError? error)
    : Exception(error?.Description ?? $"The server returned {(int)status}.")
{
    public HttpStatusCode Status { get; } = status;
    public ApiError? Error { get; } = error;
    public string? Code => Error?.Code;
}
