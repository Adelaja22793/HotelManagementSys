namespace HMS.Api.Middleware;

/// <summary>
/// Hardening headers for an API that only returns JSON and files: nothing may be framed, sniffed or run as a page,
/// and responses never carry the referrer elsewhere.
/// </summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            if (!context.Request.Path.StartsWithSegments("/openapi"))
                headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
            if (context.Request.Path.StartsWithSegments("/api") && !headers.ContainsKey("Cache-Control"))
                headers.CacheControl = "no-store";
            return Task.CompletedTask;
        });
        return next(context);
    }
}
