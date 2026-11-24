using HeartLog.BLL.Interfaces;
using HeartLog.BLL.Models.Agreements;
using HeartLog.DAL.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace HeartLog.Api.Services;

public sealed class JournalConsentFilter(ApplicationDbContext db, ICurrentUserService users) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = await users.GetCurrentUserAsync(context.HttpContext.User, context.HttpContext.RequestAborted);
        var latest = await db.AgreementEvents.AsNoTracking()
            .Where(x => x.UserId == user.Id && x.AgreementKey == AgreementCatalog.JournalProcessing)
            .OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(context.HttpContext.RequestAborted);
        if (latest?.Action != "granted" || latest.Version != AgreementCatalog.JournalProcessingVersion)
        {
            context.Result = new ObjectResult(new { code = "journal_consent_required", message = "Journal processing consent is required." })
            { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }
        await next();
    }
}
