using System.Security.Claims;
using System.Threading.RateLimiting;
using HMS.Api;
using HMS.Api.Authorization;
using HMS.Api.Middleware;
using HMS.Api.Services;
using HMS.Application;
using HMS.Application.Abstractions;
using HMS.Contracts.Common;
using HMS.Infrastructure;
using HMS.Infrastructure.Identity;
using HMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services
    .AddControllers(options => options.Filters.Add<BranchAccessFilter>())
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(e => e.Value?.Errors.Count > 0)
                .ToDictionary(e => e.Key, e => e.Value!.Errors.Select(x => string.IsNullOrEmpty(x.ErrorMessage) ? "Invalid value." : x.ErrorMessage).ToArray());
            return new BadRequestObjectResult(ApiError.Create(ErrorCodes.ValidationFailed, "One or more fields are invalid.",
                context.HttpContext.TraceIdentifier, errors));
        };
    });

// Authentication: short-lived JWT access tokens + rotating refresh tokens.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, jwt) =>
    {
        var settings = jwt.Value;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = settings.Issuer,
            ValidAudience = settings.Audience,
            IssuerSigningKey = TokenService.GetSigningKey(settings),
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = JwtRegisteredClaimNames.Name,
        };
        options.Events = new JwtBearerEvents
        {
            // Reject tokens of users that were deactivated or whose access changed after issue.
            OnTokenValidated = async context =>
            {
                var userId = context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                var stamp = context.Principal?.FindFirst(HmsClaimTypes.SecurityStamp)?.Value;
                var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
                var user = userId is null ? null : await users.FindByIdAsync(userId);
                if (user is null || !user.IsActive || user.SecurityStamp != stamp)
                    context.Fail("Session is no longer valid.");
            },
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(ApiError.Create(ErrorCodes.Unauthenticated,
                    "Authentication is required.", context.HttpContext.TraceIdentifier));
            },
            OnForbidden = async context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(ApiError.Create(ErrorCodes.Forbidden,
                    "You do not have permission to perform this action.", context.HttpContext.TraceIdentifier));
            },
        };
    });

builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicies.Auth, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = context.RequestServices.GetRequiredService<IConfiguration>().GetValue("RateLimiting:AuthPermitPerMinute", 10),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
    // Public booking: generous for browsing, tight for anything that holds rooms or starts a payment.
    options.AddPolicy(RateLimitPolicies.PublicRead, context => RateLimitPartition.GetFixedWindowLimiter(
        "read:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = context.RequestServices.GetRequiredService<IConfiguration>().GetValue("RateLimiting:PublicReadPerMinute", 60),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
    options.AddPolicy(RateLimitPolicies.PublicWrite, context => RateLimitPartition.GetFixedWindowLimiter(
        "write:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = context.RequestServices.GetRequiredService<IConfiguration>().GetValue("RateLimiting:PublicWritePerMinute", 10),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
    options.AddPolicy(RateLimitPolicies.Upload, context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = context.RequestServices.GetRequiredService<IConfiguration>().GetValue("RateLimiting:UploadsPerMinute", 20),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
    options.OnRejected = async (context, ct) =>
    {
        await context.HttpContext.Response.WriteAsJsonAsync(ApiError.Create(ErrorCodes.RateLimited,
            "Too many requests. Please wait and try again.", context.HttpContext.TraceIdentifier), ct);
    };
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<HmsDbContext>("database", tags: ["ready"]);

// The booking website is the only browser client; staff use the desktop app, which needs no CORS.
builder.Services.AddCors();
builder.Services.AddOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>().Configure<IConfiguration>((options, config) =>
    options.AddPolicy(HMS.Api.Controllers.PublicCors.Policy, policy => policy
        .WithOrigins(config.GetSection("PublicBooking:AllowedOrigins").Get<string[]>() ?? [])
        .WithMethods("GET", "POST", "PUT", "DELETE")
        .WithHeaders("Content-Type", HMS.Api.Controllers.PublicBookingController.TokenHeader)
        .SetPreflightMaxAge(TimeSpan.FromHours(1))));

builder.Services.Configure<BackgroundJobsOptions>(builder.Configuration.GetSection(BackgroundJobsOptions.SectionName));
builder.Services.AddSingleton<BackgroundJobRunner>();
builder.Services.AddHostedService<BackgroundJobsWorker>();

builder.Services.AddOpenApi();

var app = builder.Build();

// The sandbox gateway confirms bookings without real money; it must never run in production.
if (string.Equals(app.Configuration["PublicBooking:PaymentProvider"], "Sandbox", StringComparison.OrdinalIgnoreCase)
    && !app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
    throw new InvalidOperationException("PublicBooking:PaymentProvider=Sandbox is only allowed in Development.");

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();
app.MapControllers();

await DatabaseInitializer.InitializeAsync(app.Services);

app.Run();

public partial class Program;
