using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HeartLog.BLL.Models.Agreements;

namespace HeartLog.BLL.Tests;

public sealed class LegalReleaseLoaderTests
{
    [Fact]
    public void DraftLoadsOnlyAfterExactByteHashVerification()
    {
        using var fixture = new Fixture();
        var release = LegalReleaseLoader.Load(fixture.Selector, fixture.Root, false);
        Assert.Equal("draft-v1", release.Terms.Version);
        Assert.Equal("saved-entries-sensitive-data", release.Consent.Id);
        File.AppendAllText(Path.Combine(fixture.Root, "terms.md"), "changed");
        Assert.Throws<InvalidDataException>(() => LegalReleaseLoader.Load(fixture.Selector, fixture.Root, false));
    }

    [Fact]
    public void DraftIsRejectedForPublicationAndMissingFileFailsClosed()
    {
        using var fixture = new Fixture();
        Assert.Throws<InvalidDataException>(() => LegalReleaseLoader.Load(fixture.Selector, fixture.Root, true));
        File.Delete(Path.Combine(fixture.Root, "privacy.md"));
        Assert.Throws<FileNotFoundException>(() => LegalReleaseLoader.Load(fixture.Selector, fixture.Root, false));
    }

    [Theory]
    [InlineData("\uFEFFTerms\n")]
    [InlineData("Terms\r\n")]
    public void RejectsBomAndCarriageReturnsEvenBeforeHashComparison(string text)
    {
        using var fixture = new Fixture();
        File.WriteAllText(Path.Combine(fixture.Root, "terms.md"), text, Encoding.UTF8);
        Assert.Throws<InvalidDataException>(() => LegalReleaseLoader.Load(fixture.Selector, fixture.Root, false));
    }

    [Fact]
    public void RejectsInvalidUtf8AndPublishedPlaceholder()
    {
        using var invalid = new Fixture();
        File.WriteAllBytes(Path.Combine(invalid.Root, "terms.md"), [0xff]);
        Assert.Throws<DecoderFallbackException>(() => LegalReleaseLoader.Load(invalid.Selector, invalid.Root, false));
        using var placeholder = new Fixture("published", "LEGAL_PLACEHOLDER\n", "v1");
        Assert.Throws<InvalidDataException>(() => LegalReleaseLoader.Load(placeholder.Selector, placeholder.Root, true));
        using var publishedDraftVersion = new Fixture("published");
        Assert.Throws<InvalidDataException>(() => LegalReleaseLoader.Load(publishedDraftVersion.Selector, publishedDraftVersion.Root, true));
        using var published = new Fixture("published", "Approved synthetic terms\n", "v1");
        Assert.Equal("published", LegalReleaseLoader.Load(published.Selector, published.Root, true).Status);
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "heartlog-legal-test-" + Guid.NewGuid().ToString("N"));
        public string Selector => Path.Combine(Root, "selector.json");

        public Fixture(string status = "draft", string termsText = "Terms\n", string version = "draft-v1")
        {
            Directory.CreateDirectory(Root);
            var consent = "{\"schemaVersion\":1,\"entries\":[{\"id\":\"saved-entries-sensitive-data\",\"version\":\"" + version + "\",\"checkboxLabel\":\"agree\",\"statement\":\"statement\",\"explanation\":\"explanation\",\"withdrawal\":\"withdrawal\"}]}\n";
            object Add(string file, string content)
            {
                var bytes = Encoding.UTF8.GetBytes(content);
                File.WriteAllBytes(Path.Combine(Root, file), bytes);
                return new { version, file, sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) };
            }
            var terms = Add("terms.md", termsText);
            var privacy = Add("privacy.md", "Privacy\n");
            var entries = Add("consent.json", consent);
            File.WriteAllText(Path.Combine(Root, "release.json"), JsonSerializer.Serialize(new
            {
                release = status == "published" ? "legal-1" : "draft", language = "en", status,
                documents = new { terms, privacy }, consents = new { savedEntries = entries }
            }));
            File.WriteAllText(Selector, "{\"manifest\":\"release.json\"}");
        }

        public void Dispose() => Directory.Delete(Root, true);
    }
}
