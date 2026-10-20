using HMS.Contracts.Common;

namespace HMS.Application.Common;

/// <summary>Base for expected failures that map to a stable API error contract.</summary>
public abstract class AppException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public sealed class ValidationAppException : AppException
{
    public ValidationAppException(IReadOnlyDictionary<string, string[]> errors, string message = "One or more fields are invalid.")
        : base(ErrorCodes.ValidationFailed, message, 400) => Errors = errors;

    public ValidationAppException(string field, string error)
        : this(new Dictionary<string, string[]> { [field] = [error] }) { }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

public sealed class NotFoundException(string entity, object key)
    : AppException(ErrorCodes.NotFound, $"{entity} '{key}' was not found.", 404);

public sealed class ForbiddenException(string code = ErrorCodes.Forbidden, string message = "You do not have access to this resource.")
    : AppException(code, message, 403);

public sealed class UnauthenticatedException(string code = ErrorCodes.Unauthenticated, string message = "Authentication is required.")
    : AppException(code, message, 401);

/// <summary>State or concurrency conflict (HTTP 409).</summary>
public sealed class ConflictException(string code, string message)
    : AppException(code, message, 409);
