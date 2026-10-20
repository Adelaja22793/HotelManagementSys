namespace HMS.Contracts.Cashier;

public sealed record OpenShiftRequest(decimal OpeningFloat);

/// <summary>Counts declared by the cashier. Expected totals are computed by the server.</summary>
public sealed record CloseShiftRequest(decimal CountedCash, decimal VerifiedPos, decimal VerifiedTransfer, string? VarianceExplanation,
    Guid ConcurrencyStamp);

public sealed record ApproveVarianceRequest(Guid ConcurrencyStamp);

/// <summary>Status: Open, Closing (awaiting variance approval), Closed.</summary>
public sealed record CashierShiftDto(
    Guid Id,
    string CashierUserId,
    string Status,
    DateOnly BusinessDate,
    DateTime OpenedAtUtc,
    DateTime? ClosedAtUtc,
    decimal OpeningFloat,
    decimal RunningCash,
    decimal RunningPos,
    decimal RunningTransfer,
    decimal? ExpectedCash,
    decimal? ExpectedPos,
    decimal? ExpectedTransfer,
    decimal? CountedCash,
    decimal? VerifiedPos,
    decimal? VerifiedTransfer,
    decimal? CashVariance,
    decimal? PosVariance,
    decimal? TransferVariance,
    string? VarianceExplanation,
    string? ApprovedByUserId,
    Guid ConcurrencyStamp);
