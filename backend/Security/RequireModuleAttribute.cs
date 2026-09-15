using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Services;

namespace Tiaano.Vms.Api.Security;

/// <summary>Backend-authoritative module entitlement check. Frontend hiding is not security.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequireModuleAttribute : Attribute, IAsyncActionFilter
{
    public string ModuleKey { get; }

    public RequireModuleAttribute(string moduleKey) => ModuleKey = moduleKey;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var entitlements = context.HttpContext.RequestServices.GetService(typeof(IEntitlementService)) as IEntitlementService;
        if (entitlements is null)
        {
            context.Result = new ObjectResult(new ApiResponse<object>(false, null, "Entitlement service unavailable."))
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
            return;
        }

        try
        {
            await entitlements.EnsureModuleEnabledAsync(ModuleKey);
        }
        catch (UnauthorizedAccessException ex)
        {
            context.Result = new ObjectResult(new ApiResponse<object>(false, null, ex.Message))
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }
}
