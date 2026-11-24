using HeartLog.BLL.Models.Agreements;
using HeartLog.DAL.Data;
using Microsoft.EntityFrameworkCore;

namespace HeartLog.Api.Services;

public sealed class JournalConsentService(ApplicationDbContext db)
{
    public async Task<string> GetStatusAsync(Guid userId, CancellationToken cancellationToken)
    {
        var action = await db.AgreementEvents.AsNoTracking()
            .Where(x => x.UserId == userId && x.AgreementKey == AgreementCatalog.JournalProcessing)
            .OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id)
            .Select(x => x.Action).FirstOrDefaultAsync(cancellationToken);
        return action switch { "granted" => "granted", "withdrawn" => "withdrawn", _ => "notGranted" };
    }
}
