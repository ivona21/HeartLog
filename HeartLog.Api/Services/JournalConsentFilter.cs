using HeartLog.BLL.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HeartLog.Api.Services;

public sealed class JournalConsentFilter(JournalConsentService consent, ICurrentUserService users) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = await users.GetCurrentUserAsync(context.HttpContext.User, context.HttpContext.RequestAborted);
        if (await consent.GetStatusAsync(user.Id, context.HttpContext.RequestAborted) != "granted")
        {
            context.Result = new ObjectResult(new { code = "journal_consent_required", message = "Journal processing consent is required." })
            { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }
        await next();
    }
}
