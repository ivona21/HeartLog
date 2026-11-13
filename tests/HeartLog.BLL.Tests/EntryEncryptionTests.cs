using System.Security.Cryptography;
using System.Text.Json.Nodes;
using HeartLog.BLL.Services.Encryption;

namespace HeartLog.BLL.Tests;

public sealed class EntryEncryptionTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid EntryId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid OtherId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private const string Note = "Danas osjećam sreću i mir.\nČujem ptice, čitam i uživam u tišini. Život je lijep.";
    private static readonly List<StoredEmotion> Emotions =
    [
        new(Guid.Parse("44444444-4444-4444-8444-444444444444"), true),
        new(Guid.Parse("55555555-5555-4555-8555-555555555555"), false)
    ];

    // Synthetic key used only by these tests; no environment or application config is loaded.
    private static EntryEncryption CreateEncryption() =>
        new(Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()));

    [Fact]
    public void EncryptDecrypt_PreservesMultilineUnicodeNoteAndSelectedEmotions()
    {
        var writer = CreateEncryption();
        var encryptedNote = writer.Encrypt(OwnerId, EntryId, "note", Note);
        var encryptedEmotions = writer.Encrypt(OwnerId, EntryId, "emotions", Emotions);
        var reader = CreateEncryption();

        Assert.Equal(Note, reader.Decrypt<string>(OwnerId, EntryId, "note", encryptedNote));
        Assert.Equal(Emotions, reader.Decrypt<List<StoredEmotion>>(OwnerId, EntryId, "emotions", encryptedEmotions));
    }

    [Fact]
    public void Decrypt_RejectsTamperedCiphertext()
    {
        var encryption = CreateEncryption();
        foreach (var (field, encrypted) in EncryptFields(encryption))
        {
            // Keep the bundle valid JSON/Base64 and alter an actual ciphertext byte.
            var bundle = JsonNode.Parse(encrypted)!.AsObject();
            var ciphertext = Convert.FromBase64String(bundle["ciphertext"]!.GetValue<string>());
            ciphertext[0] ^= 1;
            bundle["ciphertext"] = Convert.ToBase64String(ciphertext);

            Assert.Throws<CryptographicException>(() =>
                encryption.Decrypt<object>(OwnerId, EntryId, field, bundle.ToJsonString()));
        }
    }

    [Fact]
    public void Decrypt_RejectsMismatchedOwner()
    {
        var encryption = CreateEncryption();
        foreach (var (field, encrypted) in EncryptFields(encryption))
            Assert.Throws<CryptographicException>(() =>
                encryption.Decrypt<object>(OtherId, EntryId, field, encrypted));
    }

    [Fact]
    public void Decrypt_RejectsMismatchedEntry()
    {
        var encryption = CreateEncryption();
        foreach (var (field, encrypted) in EncryptFields(encryption))
            Assert.Throws<CryptographicException>(() =>
                encryption.Decrypt<object>(OwnerId, OtherId, field, encrypted));
    }

    [Fact]
    public void Decrypt_RejectsMismatchedField()
    {
        var encryption = CreateEncryption();
        foreach (var (field, encrypted) in EncryptFields(encryption))
            Assert.Throws<CryptographicException>(() =>
                encryption.Decrypt<object>(OwnerId, EntryId, field == "note" ? "emotions" : "note", encrypted));
    }

    private static (string Field, string Encrypted)[] EncryptFields(EntryEncryption encryption) =>
    [
        ("note", encryption.Encrypt(OwnerId, EntryId, "note", Note)),
        ("emotions", encryption.Encrypt(OwnerId, EntryId, "emotions", Emotions))
    ];
}
