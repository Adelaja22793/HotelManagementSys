namespace HMS.Contracts.Users;

public sealed record UserDto(
    string Id,
    string Email,
    string FullName,
    bool IsActive,
    bool MustChangePassword,
    IReadOnlyList<string> Roles,
    IReadOnlyList<UserBranchDto> Branches);

public sealed record UserBranchDto(Guid BranchId, string BranchCode, bool IsDefault);

/// <summary>
/// Creates a staff account with a one-time password the user must change at first sign-in.
/// </summary>
public sealed record CreateUserRequest(
    string Email,
    string FullName,
    string TemporaryPassword,
    IReadOnlyList<string> Roles,
    IReadOnlyList<Guid> BranchIds,
    Guid? DefaultBranchId);

public sealed record UpdateUserAccessRequest(
    IReadOnlyList<string> Roles,
    IReadOnlyList<Guid> BranchIds,
    Guid? DefaultBranchId,
    bool IsActive);
