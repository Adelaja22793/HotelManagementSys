using HMS.Application.Abstractions;
using HMS.Application.Auditing;
using HMS.Application.Branches;
using HMS.Application.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HMS.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        services.AddScoped<IBranchAccessService, BranchAccessService>();
        services.AddScoped<IBranchService, BranchService>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();
        return services;
    }
}
