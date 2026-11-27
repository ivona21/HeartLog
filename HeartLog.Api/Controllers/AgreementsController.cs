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
    private readonly LegalRelease _legal;
    private readonly bool _allowLegacy;

    public AgreementsController(ApplicationDbContext db, ICurrentUserService currentUser, IUserRepository users, LegalRelease legal, IConfiguration configuration)
    {
        _db = db;
        _currentUser = currentUser;
        _users = users;
        _legal = legal;
        _allowLegacy = configuration.GetValue<bool>("Legal:AllowLegacyClients");
    }

    [AllowAnonymous]
    [HttpGet("requirements")]
    [ProducesResponseType(typeof(IReadOnlyList<CurrentLegalRequirement>), StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "Agreements_GetRequirements")]
    public ActionResult<IReadOnlyList<CurrentLegalRequirement>> GetRequirements() => Ok(Requirements());

    private CurrentLegalRequirement[] Requirements() =>
    [
        new(AgreementCatalog.Terms, _legal.Terms.Version, _legal.Terms.Sha256, AgreementCatalog.TermsUrl, AgreementCatalog.TermsStatement),
        new(AgreementCatalog.PrivacyPolicy, _legal.Privacy.Version, _legal.Privacy.Sha256, AgreementCatalog.PrivacyPolicyUrl, AgreementCatalog.TermsStatement)
    ];

    [AllowAnonymous]
    [HttpGet("journal-consent/statement")]
    [SwaggerOperation(OperationId = "Agreements_GetJournalConsentStatement")]
    public ActionResult<JournalConsentStatement> GetJournalConsentStatement() =>
        Ok(new JournalConsentStatement(_legal.Consent.Id, _legal.Consent.Version, _legal.ConsentFile.Sha256,
            _legal.Consent.CheckboxLabel, _legal.Consent.Statement, _legal.Consent.Explanation, _legal.Consent.Withdrawal));

    [Authorize]
    [HttpPost("journal-consent")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(LegalConflictResponse), StatusCodes.Status409Conflict)]
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
                    existing[0].Action != "granted" || existing[0].Statement != submission.Statement ||
                    (existing[0].ConsentId ?? "") != (submission.ConsentId ?? "") || (existing[0].Version ?? "") != (submission.ConsentVersion ?? "") ||
                    (existing[0].ContentHash ?? "") != (submission.ContentHash ?? "");
                return;
            }
            var current = submission.Statement == _legal.Consent.Statement && submission.ConsentId == _legal.Consent.Id &&
                submission.ConsentVersion == _legal.Consent.Version && submission.ContentHash == _legal.ConsentFile.Sha256;
            var legacy = _allowLegacy && submission.Statement == AgreementCatalog.JournalStatement &&
                string.IsNullOrEmpty(submission.ConsentId) && string.IsNullOrEmpty(submission.ConsentVersion) && string.IsNullOrEmpty(submission.ContentHash);
            if (!current && !legacy)
            {
                statementChanged = true;
                return;
            }
            _db.AgreementEvents.Add(new AgreementEvent
            {
                UserId = user.Id, AgreementKey = AgreementCatalog.JournalProcessing,
                Action = "granted", Statement = submission.Statement, Source = "journalPrompt",
                ConsentId = string.IsNullOrEmpty(submission.ConsentId) ? null : submission.ConsentId,
                Version = string.IsNullOrEmpty(submission.ConsentVersion) ? null : submission.ConsentVersion,
                ContentHash = string.IsNullOrEmpty(submission.ContentHash) ? null : submission.ContentHash,
                OccurredAtUtc = DateTime.UtcNow, SubmissionId = submission.SubmissionId
            });
            await _db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
        if (conflict) return Conflict(new LegalConflictResponse("submission_id_reused"));
        if (statementChanged) return Conflict(new LegalConflictResponse("legal_content_outdated"));
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
        return Ok(Requirements().Select(requirement =>
        {
            var latest = events.Where(x => x.AgreementKey == requirement.Key)
                .OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id).FirstOrDefault();
            return new
            {
                requirement.Key, requirement.Version, requirement.Sha256, requirement.Url, requirement.Statement,
                AcceptedVersion = latest?.Version,
                Action = latest?.Action,
                ActionRequired = latest is null || latest.Action == "withdrawn" || latest.Version != requirement.Version || latest.ContentHash != requirement.Sha256
            };
        }));
    }

    [Authorize]
    [HttpPost("me")]
    [ProducesResponseType(typeof(LegalConflictResponse), StatusCodes.Status409Conflict)]
    [SwaggerOperation(OperationId = "Agreements_Submit")]
    public async Task<IActionResult> Accept([FromBody] AgreementSubmission submission, CancellationToken cancellationToken)
    {
        if (submission.SubmissionId == Guid.Empty || submission.Items is null || submission.Items.Count == 0 ||
            submission.Items.Any(x => x is null || !IsSupported(x)) ||
            submission.Items.Select(x => x.Key).Distinct().Count() != submission.Items.Count)
            return Conflict(new LegalConflictResponse("legal_content_outdated"));

        var current = await _currentUser.GetCurrentUserAsync(User, cancellationToken);
        if (current.SupabaseUserId is not Guid providerId) return Unauthorized();
        var conflict = false;
        await _users.WithProviderLockAsync(providerId, async () =>
        {
            var user = await _users.GetBySupabaseUserIdAsync(providerId);
            if (user is null || user.DeletionRequestedAt is not null) throw new UnauthorizedAccessException();
            var prior = await _db.AgreementEvents.AsNoTracking()
                .Where(x => x.UserId == user.Id && x.SubmissionId == submission.SubmissionId)
                .ToListAsync(cancellationToken);
            if (prior.Count > 0)
            {
                conflict = prior.Count != submission.Items.Count || submission.Items.Any(item =>
                    !prior.Any(x => x.AgreementKey == item.Key && x.Version == item.Version && (x.ContentHash ?? "") == (item.ContentHash ?? "")));
                return;
            }

            var now = DateTime.UtcNow;
            _db.AgreementEvents.AddRange(submission.Items.Select(item => new AgreementEvent
            {
                UserId = user.Id, AgreementKey = item.Key, Version = item.Version,
                ContentHash = string.IsNullOrEmpty(item.ContentHash) ? null : item.ContentHash,
                Action = item.Key == AgreementCatalog.PrivacyPolicy ? "acknowledged" : "accepted",
                Source = "agreementPrompt", OccurredAtUtc = now, SubmissionId = submission.SubmissionId
            }));
            await _db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
        return conflict ? Conflict(new LegalConflictResponse("submission_id_reused")) : NoContent();
    }

    private bool IsSupported(AgreementSubmissionItem item)
    {
        if (Requirements().Any(r => r.Key == item.Key && r.Version == item.Version && r.Sha256 == item.ContentHash)) return true;
        return _allowLegacy && string.IsNullOrEmpty(item.ContentHash) && AgreementCatalog.IsCurrent(item.Key, item.Version);
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
    public string ContentHash { get; set; } = string.Empty;
}

public sealed record CurrentLegalRequirement(string Key, string Version, string Sha256, string Url, string Statement);
public sealed record LegalConflictResponse(string Code);
public sealed record JournalConsentStatement(string ConsentId, string ConsentVersion, string ContentHash,
    string CheckboxLabel, string Statement, string Explanation, string Withdrawal);

public sealed class JournalConsentSubmission
{
    public Guid SubmissionId { get; set; }
    public bool JournalProcessingConsent { get; set; }
    public string? Statement { get; set; }
    public string? ConsentId { get; set; }
    public string? ConsentVersion { get; set; }
    public string? ContentHash { get; set; }
}
