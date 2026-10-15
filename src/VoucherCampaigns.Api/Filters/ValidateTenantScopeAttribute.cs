using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace VoucherCampaigns.Api.Filters;

public class ValidateTenantScopeAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var user = context.HttpContext.User;
        if (!user.Identity?.IsAuthenticated ?? true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var userScopeId = user.FindFirst("escopoId")?.Value;
        var requestScopeId = context.HttpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();

        if (!string.IsNullOrEmpty(requestScopeId) && requestScopeId != userScopeId)
        {
            context.Result = new ObjectResult(new { error = "Forbidden", message = "Tentativa de operação fora do escopo organizacional." })
            {
                StatusCode = 403
            };
        }
        base.OnActionExecuting(context);
    }
}