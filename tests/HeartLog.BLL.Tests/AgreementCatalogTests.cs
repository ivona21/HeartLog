using HeartLog.BLL.Models.Agreements;

namespace HeartLog.BLL.Tests;

public class AgreementCatalogTests
{
    [Fact]
    public void RequirementsHaveUniqueKeysAndRejectUnknownVersions()
    {
        Assert.Equal(3, AgreementCatalog.Requirements.Select(x => x.Key).Distinct().Count());
        foreach (var requirement in AgreementCatalog.Requirements)
        {
            Assert.True(AgreementCatalog.IsCurrent(requirement.Key, requirement.Version));
            Assert.False(AgreementCatalog.IsCurrent(requirement.Key, "old"));
        }
        Assert.False(AgreementCatalog.IsCurrent("unknown", AgreementCatalog.TermsVersion));
    }

    [Fact]
    public void RequirementsUsePublicLegalUrlsAndDescribeExplicitConsent()
    {
        Assert.Equal("https://heartlog.me/terms", AgreementCatalog.Requirements.Single(x => x.Key == AgreementCatalog.Terms).Url);
        Assert.Equal("https://heartlog.me/privacy-policy", AgreementCatalog.Requirements.Single(x => x.Key == AgreementCatalog.PrivacyPolicy).Url);
        Assert.Contains("explicitly consent", AgreementCatalog.Requirements.Single(x => x.Key == AgreementCatalog.JournalProcessing).Statement);
    }
}
