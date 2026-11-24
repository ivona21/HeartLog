using System.Security.Cryptography;
using System.Text;
using HeartLog.Api.DTOs;
using HeartLog.BLL.Interfaces;
using HeartLog.BLL.Models.Agreements;
using HeartLog.BLL.Models.Auth;
using HeartLog.DAL.Data;
using HeartLog.DAL.Interfaces;
using HeartLog.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace HeartLog.Api.Services;

public sealed class RegistrationAgreementService(
    ApplicationDbContext db, IExternalAuthService provider, IUserRepository users)
{
    public static bool Valid(RegistrationAgreements? a) => a is not null &&
        a.TermsAccepted && a.PrivacyPolicyAcknowledged && a.JournalProcessingConsent &&
        AgreementCatalog.IsCurrent(AgreementCatalog.Terms, a.TermsVersion) &&
        AgreementCatalog.IsCurrent(AgreementCatalog.PrivacyPolicy, a.PrivacyPolicyVersion) &&
        AgreementCatalog.IsCurrent(AgreementCatalog.JournalProcessing, a.JournalProcessingVersion);

    public async Task<ExternalAuthRegistrationResult> RegisterAsync(string email, string password, RegistrationAgreements agreements)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var now = DateTime.UtcNow;
        await db.RegistrationIntents.Where(x => x.ExpiresAtUtc <= now).ExecuteDeleteAsync();
        var intent = new RegistrationIntent
        {
            TokenHash = Hash(token), TermsVersion = agreements.TermsVersion,
            PrivacyPolicyVersion = agreements.PrivacyPolicyVersion,
            JournalProcessingVersion = agreements.JournalProcessingVersion,
            SubmittedAtUtc = now, ExpiresAtUtc = now.AddDays(7)
        };
        db.RegistrationIntents.Add(intent);
        await db.SaveChangesAsync();
        var registration = await provider.RegisterWithIntentAsync(email, password, token);
        intent.ProviderUserId = registration.ProviderUserId;
        await db.SaveChangesAsync();
        return registration;
    }

    public async Task ConsumeAsync(ExternalAuthSession session, CancellationToken cancellationToken)
    {
        var token = await provider.GetRegistrationIntentAsync(session.AccessToken, session.User.ProviderUserId, cancellationToken);
        if (token is null || token.Length != 64 || !token.All(Uri.IsHexDigit)) return;
        var hash = Hash(token);
        await users.WithProviderLockAsync(session.User.ProviderUserId, async () =>
        {
            var user = await users.GetBySupabaseUserIdAsync(session.User.ProviderUserId);
            if (user is null || user.DeletionRequestedAt is not null) return;
            var now = DateTime.UtcNow;
            var intent = await db.RegistrationIntents.SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
            if (intent is null || intent.ProviderUserId != session.User.ProviderUserId || intent.ConsumedAtUtc is not null || intent.ExpiresAtUtc <= now) return;
            // Lock the intent row independently of the provider lock, so a copied token cannot be consumed twice.
            var changed = await db.RegistrationIntents.Where(x => x.Id == intent.Id && x.ConsumedAtUtc == null && x.ExpiresAtUtc > now)
                .ExecuteUpdateAsync(x => x.SetProperty(i => i.ConsumedAtUtc, now), cancellationToken);
            if (changed != 1) return;
            db.AgreementEvents.AddRange(new[]
            {
                Create(AgreementCatalog.Terms, intent.TermsVersion, "accepted"),
                Create(AgreementCatalog.PrivacyPolicy, intent.PrivacyPolicyVersion, "acknowledged"),
                Create(AgreementCatalog.JournalProcessing, intent.JournalProcessingVersion, "granted")
            });
            await db.SaveChangesAsync(cancellationToken);
            AgreementEvent Create(string key, string version, string action) => new()
            {
                UserId = user.Id, AgreementKey = key, Version = version, Action = action,
                Source = "registration", OccurredAtUtc = intent.SubmittedAtUtc, SubmissionId = intent.Id
            };
        }, cancellationToken);
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
