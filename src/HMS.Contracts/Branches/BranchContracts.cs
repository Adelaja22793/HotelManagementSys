namespace HMS.Contracts.Branches;

public sealed record BranchSummaryDto(Guid Id, string Code, string Name, string TimeZoneId, bool IsDefault);

public sealed record BranchDto(
    Guid Id,
    Guid CompanyId,
    string Code,
    string Name,
    string TimeZoneId,
    string Currency,
    string? AddressLine,
    string? City,
    string? Phone,
    TimeOnly BusinessDayCutoffLocal,
    DateOnly CurrentBusinessDate,
    bool IsActive,
    Guid ConcurrencyStamp);

public sealed record CreateBranchRequest(
    string Code,
    string Name,
    string? TimeZoneId,
    string? Currency,
    string? AddressLine,
    string? City,
    string? Phone);

public sealed record UpdateBranchRequest(
    string Name,
    string TimeZoneId,
    string Currency,
    string? AddressLine,
    string? City,
    string? Phone,
    TimeOnly? BusinessDayCutoffLocal,
    bool IsActive,
    Guid ConcurrencyStamp);
