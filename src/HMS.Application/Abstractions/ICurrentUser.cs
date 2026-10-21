namespace HMS.Application.Abstractions;

/// <summary>The authenticated caller, as established by the API from a validated token.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    string? UserId { get; }
    Guid? CompanyId { get; }
    IReadOnlyCollection<string> Roles { get; }
    string? TraceId { get; }
    /// <summary>The user's full name from the token, for display and signatures.</summary>
    string? DisplayName => null;
    bool IsInRole(string role);
}
