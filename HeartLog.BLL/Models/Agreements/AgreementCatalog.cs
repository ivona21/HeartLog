namespace HeartLog.BLL.Models.Agreements;

/// <summary>Versions identify immutable texts, not the dates on which users accepted them.</summary>
public static class AgreementCatalog
{
    public const string Terms = "terms";
    public const string PrivacyPolicy = "privacyPolicy";
    public const string JournalProcessing = "journalProcessing";
    public const string TermsVersion = "2026-11-23";
    public const string PrivacyPolicyVersion = "2026-11-23";

    public const string TermsUrl = "https://heartlog.me/terms";
    public const string PrivacyPolicyUrl = "https://heartlog.me/privacy-policy";

    public const string TermsStatement = "I agree to the Terms of Service and acknowledge the Privacy Policy.";
    public const string JournalStatement = "I explicitly consent to HeartLog processing the emotions and journal entries I choose to save, including information they may reveal about my health, religious beliefs or sexual orientation, so that HeartLog can save and display my entries. I can withdraw this consent at any time in Settings.";

    public static readonly IReadOnlyList<AgreementRequirement> Requirements = Array.AsReadOnly<AgreementRequirement>(
    [
        new(Terms, TermsVersion, TermsUrl, TermsStatement),
        new(PrivacyPolicy, PrivacyPolicyVersion, PrivacyPolicyUrl, TermsStatement)
    ]);

    public static bool IsCurrent(string key, string version) =>
        Requirements.Any(requirement => requirement.Key == key && requirement.Version == version);
}

public sealed record AgreementRequirement(string Key, string Version, string? Url, string Statement);
