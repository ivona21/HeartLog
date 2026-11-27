using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HeartLog.BLL.Models.Agreements;

public sealed record LegalFile(string Version, string File, string Sha256, string Content);
public sealed record LegalConsent(string Id, string Version, string CheckboxLabel, string Statement, string Explanation, string Withdrawal);
public sealed record LegalRelease(string Release, string Language, string Status, LegalFile Terms, LegalFile Privacy, LegalFile ConsentFile, LegalConsent Consent);

/// <summary>Loads only checked-in bytes; never contacts a remote repository.</summary>
public static class LegalReleaseLoader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex Placeholder = new("PLACEHOLDER|LEGAL_PLACEHOLDER|has not yet been supplied|replace-with-approved", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // These hashes identify the pre-existing snapshots; they are not backfilled into user records.
    public static void VerifyHistoricalSnapshots(string historyRoot)
    {
        Verify("2026-11-23/terms.md", "9354cb11a2a3106f6dcc478471d483ebdf9db3bd823b032c68e1bb47b3802568");
        Verify("2026-11-23/privacy-policy.md", "6fdd40bc52d0ee969a20719253445656158c04248179aa308f8870dd890a3ee9");
        void Verify(string path, string expected)
        {
            var bytes = File.ReadAllBytes(Resolve(historyRoot, path));
            Decode(bytes);
            if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != expected)
                throw new InvalidDataException($"Historical legal snapshot hash mismatch: {path}");
        }
    }

    public static LegalRelease Load(string selectorPath, string legalRoot, bool publicationReady)
    {
        using var selector = JsonDocument.Parse(ReadText(selectorPath));
        var manifestName = Required(selector.RootElement, "manifest");
        using var manifest = JsonDocument.Parse(ReadText(Resolve(legalRoot, manifestName)));
        var root = manifest.RootElement;
        var release = Required(root, "release");
        var language = Required(root, "language");
        var status = Required(root, "status");
        if (status is not ("draft" or "published")) throw new InvalidDataException("Invalid legal release status.");
        if (publicationReady && (status != "published" || release.Contains("draft", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Publication requires a published non-draft legal release.");
        var documents = root.GetProperty("documents");
        var consents = root.GetProperty("consents");
        var terms = ReadFile(legalRoot, documents.GetProperty("terms"), publicationReady);
        var privacy = ReadFile(legalRoot, documents.GetProperty("privacy"), publicationReady);
        var consentFile = ReadFile(legalRoot, consents.GetProperty("savedEntries"), publicationReady);
        using var consentJson = JsonDocument.Parse(consentFile.Content);
        if (consentJson.RootElement.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidDataException("Unsupported consent schema.");
        var entries = consentJson.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        if (entries.Length != 1) throw new InvalidDataException("Expected exactly one saved-entry consent.");
        var entry = entries[0];
        var consent = new LegalConsent(Required(entry, "id"), Required(entry, "version"), Required(entry, "checkboxLabel"),
            Required(entry, "statement"), Required(entry, "explanation"), Required(entry, "withdrawal"));
        if (consent.Id != "saved-entries-sensitive-data" || consent.Version != consentFile.Version)
            throw new InvalidDataException("Consent identifier or version does not match manifest.");
        return new LegalRelease(release, language, status, terms, privacy, consentFile, consent);
    }

    private static LegalFile ReadFile(string root, JsonElement item, bool publicationReady)
    {
        var version = Required(item, "version");
        if (publicationReady && version.Contains("draft", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Draft legal versions cannot be published.");
        var file = Required(item, "file");
        var expected = Required(item, "sha256");
        if (!Regex.IsMatch(expected, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant)) throw new InvalidDataException("Invalid legal SHA-256.");
        var bytes = File.ReadAllBytes(Resolve(root, file));
        var content = Decode(bytes);
        var actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.Equals(actual, expected, StringComparison.Ordinal)) throw new InvalidDataException($"Legal content hash mismatch: {file}");
        if (publicationReady && Placeholder.IsMatch(content)) throw new InvalidDataException($"Legal placeholder content: {file}");
        return new LegalFile(version, file, expected, content);
    }

    private static string ReadText(string file) => Decode(File.ReadAllBytes(file));
    private static string Decode(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) || bytes.Contains((byte)'\r'))
            throw new InvalidDataException("Legal files must be UTF-8 without BOM and use LF newlines.");
        return StrictUtf8.GetString(bytes);
    }

    private static string Resolve(string root, string relative)
    {
        if (Path.IsPathRooted(relative)) throw new InvalidDataException("Legal paths must be relative.");
        var fullRoot = Path.GetFullPath(root);
        var full = Path.GetFullPath(relative, fullRoot);
        if (!full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("Legal path escapes the content root.");
        var current = fullRoot;
        foreach (var component in Path.GetRelativePath(fullRoot, full).Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, component);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Legal paths cannot contain symbolic links.");
        }
        return full;
    }

    private static string Required(JsonElement value, string name)
    {
        var text = value.GetProperty(name).GetString();
        return !string.IsNullOrWhiteSpace(text) ? text : throw new InvalidDataException($"Legal field is missing: {name}");
    }
}
