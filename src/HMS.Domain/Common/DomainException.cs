namespace HMS.Domain.Common;

/// <summary>Raised when a business rule or invariant is violated.</summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>The request is valid but conflicts with the record's current state (HTTP 409).</summary>
public class DomainConflictException(string code, string message) : DomainException(code, message);
