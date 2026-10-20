namespace HMS.Contracts.Guests;

public sealed record GuestDto(Guid Id, string FirstName, string LastName, string? Email, string? Phone, string? Nationality,
    string? IdDocumentType, string? IdDocumentNumber, string? Address, string? Notes, Guid ConcurrencyStamp);

public sealed record GuestSummaryDto(Guid Id, string FullName, string? Email, string? Phone);

public sealed record SaveGuestRequest(string FirstName, string LastName, string? Email, string? Phone, string? Nationality,
    string? IdDocumentType, string? IdDocumentNumber, string? Address, string? Notes, Guid? ConcurrencyStamp = null);
