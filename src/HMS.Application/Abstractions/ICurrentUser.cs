namespace HMS.Application.Abstractions;

/// <summary>The authenticated caller, as established by the API from a validated token.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    string? UserId { get; }
    Guid? CompanyId { get; }
    IReadOnlyCollection<string> Roles { get; }
    string? TraceId { get; }
    bool IsInRole(string role);
}
