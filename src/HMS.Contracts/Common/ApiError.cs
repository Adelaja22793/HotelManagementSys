namespace HMS.Contracts.Common;

/// <summary>Stable error contract returned by every failing API call.</summary>
public sealed record ApiError(
    string Code,
    string Description,
    string? TraceId,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public static ApiError Create(string code, string description, string? traceId = null,
        IReadOnlyDictionary<string, string[]>? errors = null) =>
        new(code, description, traceId, errors ?? new Dictionary<string, string[]>());
}
