namespace HMS.Domain.Common;

/// <summary>Raised when a business rule or invariant is violated.</summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
