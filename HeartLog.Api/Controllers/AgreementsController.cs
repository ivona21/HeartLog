using HeartLog.BLL.Models.Agreements;
using HeartLog.BLL.Interfaces;
using HeartLog.DAL.Data;
using HeartLog.DAL.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HeartLog.Api.Controllers;

[ApiController]
[Route("api/agreements")]
public class AgreementsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public AgreementsController(ApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    [AllowAnonymous]
    [HttpGet("requirements")]
    [ProducesResponseType(typeof(IReadOnlyList<AgreementRequirement>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<AgreementRequirement>> GetRequirements() =>
        Ok(AgreementCatalog.Requirements);

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
    {
        var user = await _currentUser.GetCurrentUserAsync(User, cancellationToken);
        var events = await _db.AgreementEvents.AsNoTracking()
            .Where(x => x.UserId == user.Id).ToListAsync(cancellationToken);
        return Ok(AgreementCatalog.Requirements.Select(requirement =>
        {
            var latest = events.Where(x => x.AgreementKey == requirement.Key)
                .OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id).FirstOrDefault();
            return new
            {
                requirement.Key, requirement.Version, requirement.Url, requirement.Statement,
                AcceptedVersion = latest?.Version,
                Action = latest?.Action,
                ActionRequired = latest is null || latest.Action == "withdrawn" || latest.Version != requirement.Version
            };
        }));
    }

    [Authorize]
    [HttpPost("me")]
    public async Task<IActionResult> Accept([FromBody] AgreementSubmission submission, CancellationToken cancellationToken)
    {
        if (submission.SubmissionId == Guid.Empty || submission.Items is null || submission.Items.Count == 0 ||
            submission.Items.Any(x => x is null || !AgreementCatalog.IsCurrent(x.Key, x.Version)) ||
            submission.Items.Select(x => x.Key).Distinct().Count() != submission.Items.Count)
            return BadRequest(new { code = "agreement_version_outdated_or_invalid" });

        var user = await _currentUser.GetCurrentUserAsync(User, cancellationToken);
        var prior = await _db.AgreementEvents.AsNoTracking()
            .Where(x => x.UserId == user.Id && x.SubmissionId == submission.SubmissionId)
            .ToListAsync(cancellationToken);
        if (prior.Count > 0)
        {
            if (prior.Count != submission.Items.Count || submission.Items.Any(item =>
                    !prior.Any(x => x.AgreementKey == item.Key && x.Version == item.Version)))
                return Conflict(new { code = "submission_id_reused" });
            return NoContent();
        }

        var now = DateTime.UtcNow;
        _db.AgreementEvents.AddRange(submission.Items.Select(item => new AgreementEvent
        {
            UserId = user.Id, AgreementKey = item.Key, Version = item.Version,
            Action = item.Key == AgreementCatalog.PrivacyPolicy ? "acknowledged" :
                item.Key == AgreementCatalog.Terms ? "accepted" : "granted",
            Source = "agreementPrompt", OccurredAtUtc = now, SubmissionId = submission.SubmissionId
        }));
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}

public sealed class AgreementSubmission
{
    public Guid SubmissionId { get; set; }
    public List<AgreementSubmissionItem> Items { get; set; } = [];
}

public sealed class AgreementSubmissionItem
{
    public string Key { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}
