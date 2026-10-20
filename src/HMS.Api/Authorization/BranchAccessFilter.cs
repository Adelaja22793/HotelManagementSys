using HMS.Application.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HMS.Api.Authorization;

/// <summary>Lets an endpoint operate on inactive branches (for example re-activating one).</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class AllowInactiveBranchAttribute : Attribute;

/// <summary>
/// Global filter: any route containing {branchId} is checked against the caller's branch membership
/// before the action runs. Services also re-check ownership of the specific records they touch.
/// </summary>
internal sealed class BranchAccessFilter(IBranchAccessService branchAccess) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.RouteData.Values.TryGetValue("branchId", out var raw) && raw is not null)
        {
            if (!Guid.TryParse(raw.ToString(), out var branchId))
                throw new Application.Common.ValidationAppException("branchId", "Invalid branch id.");

            var allowInactive = context.ActionDescriptor.EndpointMetadata.OfType<AllowInactiveBranchAttribute>().Any();
            await branchAccess.EnsureAccessAsync(branchId, allowInactive, context.HttpContext.RequestAborted);
        }
        await next();
    }
}
