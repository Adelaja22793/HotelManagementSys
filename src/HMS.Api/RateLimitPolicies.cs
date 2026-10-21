namespace HMS.Api;

internal static class RateLimitPolicies
{
    public const string Auth = "auth";
    /// <summary>Public availability and booking look-ups, per client IP.</summary>
    public const string PublicRead = "public-read";
    /// <summary>Public holds, guest details and payment starts, per client IP.</summary>
    public const string PublicWrite = "public-write";
    /// <summary>Document uploads, per user.</summary>
    public const string Upload = "upload";
}
