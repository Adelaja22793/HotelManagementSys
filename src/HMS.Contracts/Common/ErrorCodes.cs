namespace HMS.Contracts.Common;

public static class ErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string Unauthenticated = "UNAUTHENTICATED";
    public const string Forbidden = "FORBIDDEN";
    public const string BranchAccessDenied = "BRANCH_ACCESS_DENIED";
    public const string BranchInactive = "BRANCH_INACTIVE";
    public const string NotFound = "NOT_FOUND";
    public const string Conflict = "CONFLICT";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string InvalidRefreshToken = "INVALID_REFRESH_TOKEN";
    public const string RateLimited = "RATE_LIMITED";
    public const string ServerError = "SERVER_ERROR";
}
