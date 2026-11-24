using HeartLog.BLL.Models.Agreements;
using HeartLog.BLL.Interfaces;
using HeartLog.DAL.Data;
using HeartLog.DAL.Models;
using HeartLog.DAL.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

namespace HeartLog.Api.Controllers;

[ApiController]
[Route("api/agreements")]
public class AgreementsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IUserRepository _users;

    public AgreementsController(ApplicationDbContext db, ICurrentUserService currentUser, IUserRepository users)
    {
        _db = db;
        _currentUser = currentUser;
        _users = users;
    }

    [AllowAnonymous]
    [HttpGet("requirements")]
    [ProducesResponseType(typeof(IReadOnlyList<AgreementRequirement>), StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "Agreements_GetRequirements")]
    public ActionResult<IReadOnlyList<AgreementRequirement>> GetRequirements() =>
        Ok(AgreementCatalog.Requirements);

    [AllowAnonymous]
    [HttpGet("journal-consent/statement")]
    [SwaggerOperation(OperationId = "Agreements_GetJournalConsentStatement")]
    public ActionResult<JournalConsentStatement> GetJournalConsentStatement() =>
        Ok(new JournalConsentStatement(AgreementCatalog.JournalStatement));

    [Authorize]
    [HttpPost("journal-consent")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [SwaggerOperation(OperationId = "Agreements_GrantJournalConsent")]
    public async Task<IActionResult> GrantJournalConsent([FromBody] JournalConsentSubmission submission, CancellationToken cancellationToken)
    {
        if (submission.SubmissionId == Guid.Empty || !submission.JournalProcessingConsent)
            return BadRequest(new { code = "journal_consent_required" });
        var current = await _currentUser.GetCurrentUserAsync(User, cancellationToken);
        if (current.SupabaseUserId is not Guid providerId) return Unauthorized();
        var conflict = false;
        var statementChanged = false;
        await _users.WithProviderLockAsync(providerId, async () =>
        {
            var user = await _users.GetBySupabaseUserIdAsync(providerId);
            if (user is null || user.DeletionRequestedAt is not null) throw new UnauthorizedAccessException();
            var existing = await _db.AgreementEvents.AsNoTracking()
                .Where(x => x.UserId == user.Id && x.SubmissionId == submission.SubmissionId)
                .ToListAsync(cancellationToken);
            if (existing.Count != 0)
            {
                conflict = existing.Count != 1 || existing[0].AgreementKey != AgreementCatalog.JournalProcessing ||
                    existing[0].Action != "granted" || existing[0].Statement != submission.Statement;
                return;
            }
            if (!string.Equals(submission.Statement, AgreementCatalog.JournalStatement, StringComparison.Ordinal))
            {
                statementChanged = true;
                return;
            }
            _db.AgreementEvents.Add(new AgreementEvent
            {
                UserId = user.Id, AgreementKey = AgreementCatalog.JournalProcessing,
                Action = "granted", Statement = submission.Statement, Source = "journalPrompt",
                OccurredAtUtc = DateTime.UtcNow, SubmissionId = submission.SubmissionId
            });
            await _db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
        if (conflict) return Conflict(new { code = "submission_id_reused" });
        if (statementChanged) return Conflict(new { code = "journal_consent_statement_changed" });
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    [SwaggerOperation(OperationId = "Agreements_GetMine")]
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
    [SwaggerOperation(OperationId = "Agreements_Submit")]
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

public sealed record JournalConsentStatement(string Statement);

public sealed class JournalConsentSubmission
{
    public Guid SubmissionId { get; set; }
    public bool JournalProcessingConsent { get; set; }
    public string? Statement { get; set; }
}
