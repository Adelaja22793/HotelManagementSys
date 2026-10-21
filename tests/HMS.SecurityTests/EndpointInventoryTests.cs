using HMS.Api.Authorization;
using HMS.Api.IntegrationTests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HMS.SecurityTests;

/// <summary>
/// Walks every endpoint the API exposes. A new endpoint that forgets its permission, or quietly becomes anonymous,
/// fails here before it ships.
/// </summary>
public sealed class EndpointInventoryTests(HmsApiFactory factory) : IClassFixture<HmsApiFactory>
{
    /// <summary>Endpoints anyone may call. Each one is reviewed: sign-in, health, version and the public booking site.</summary>
    private static readonly string[] ApprovedAnonymous =
    [
        "POST api/v1/auth/login",
        "POST api/v1/auth/refresh",
        "GET api/v1/system/version",
        "ANY health/live",
        "ANY health/ready",
    ];

    /// <summary>Endpoints any signed-in user may call about their own account.</summary>
    private static readonly string[] ApprovedSelfService =
    [
        "POST api/v1/auth/logout",
        "GET api/v1/auth/me",
        "POST api/v1/auth/change-password",
    ];

    private IReadOnlyList<RouteEndpoint> Endpoints()
    {
        _ = factory.Server; // builds the host
        return factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .ToList();
    }

    internal static string Describe(RouteEndpoint e) =>
        $"{(e.Metadata.GetMetadata<IHttpMethodMetadata>() is { } m ? string.Join(",", m.HttpMethods) : "ANY")} {e.RoutePattern.RawText!.TrimStart('/')}";

    private static bool IsAnonymous(RouteEndpoint e) => e.Metadata.GetMetadata<IAllowAnonymous>() is not null;

    private static bool IsPublicBooking(RouteEndpoint e) => e.RoutePattern.RawText!.TrimStart('/').StartsWith("api/v1/public/", StringComparison.Ordinal);

    [Fact]
    public void Every_endpoint_needs_a_permission_unless_it_is_approved()
    {
        var problems = new List<string>();
        foreach (var e in Endpoints())
        {
            if (e.RoutePattern.RawText!.Contains("openapi", StringComparison.OrdinalIgnoreCase)) continue;
            var name = Describe(e);
            var permissions = e.Metadata.GetOrderedMetadata<HasPermissionAttribute>();
            if (IsAnonymous(e))
            {
                if (!IsPublicBooking(e) && !ApprovedAnonymous.Contains(name)) problems.Add($"{name} is anonymous but not approved");
                continue;
            }
            if (permissions.Count == 0 && !ApprovedSelfService.Contains(name)) problems.Add($"{name} has no permission");
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Every_approved_endpoint_still_exists()
    {
        var names = Endpoints().Select(Describe).ToHashSet();
        Assert.All(ApprovedAnonymous.Concat(ApprovedSelfService), n => Assert.Contains(n, names));
    }

    [Fact]
    public void Branch_routes_always_carry_the_branch_id_in_the_path()
    {
        // The branch access filter checks {branchId}; a branch route without it would skip that check.
        var problems = Endpoints()
            .Where(e => e.RoutePattern.RawText!.TrimStart('/').StartsWith("api/v1/branches/", StringComparison.Ordinal))
            .Where(e => e.RoutePattern.Parameters.All(p => p.Name != "branchId"))
            .Select(Describe)
            .ToList();
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Public_booking_endpoints_never_accept_staff_tokens_as_a_shortcut()
    {
        // The public site must not depend on staff permissions; everything there is anonymous and rate limited.
        var publicEndpoints = Endpoints().Where(IsPublicBooking).ToList();
        Assert.NotEmpty(publicEndpoints);
        Assert.All(publicEndpoints, e =>
        {
            Assert.True(IsAnonymous(e), Describe(e));
            Assert.Empty(e.Metadata.GetOrderedMetadata<HasPermissionAttribute>());
            Assert.NotNull(e.Metadata.GetMetadata<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>());
        });
    }
}
