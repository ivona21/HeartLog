using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HeartLog.BLL.Services.Encryption;

// Bundles are serialized explicitly so the PostgreSQL jsonb format is stable.
public sealed class EntryEncryption
{
    public const string CurrentKeyId = "v1";
    private readonly byte[] _key;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public EntryEncryption(string? base64Key)
    {
        try
        {
            _key = Convert.FromBase64String(base64Key ?? "");
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Entry encryption v1 key must be Base64 encoding of 32 bytes.");
        }
        if (_key.Length != 32)
            throw new InvalidOperationException("Entry encryption v1 key must be Base64 encoding of 32 bytes.");
    }

    private byte[] ResolveKey(string keyId) => keyId == CurrentKeyId
        ? _key : throw new CryptographicException("Unknown entry encryption key ID.");

    private static byte[] AssociatedData(Guid ownerId, Guid entryId, string field) =>
        Encoding.UTF8.GetBytes($"heartlog:entry:{ownerId:D}:{entryId:D}:{field}");

    public string Encrypt<T>(Guid ownerId, Guid entryId, string field, T value)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(12);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[16];
            using var aes = new AesGcm(ResolveKey(CurrentKeyId), 16);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData(ownerId, entryId, field));
            return JsonSerializer.Serialize(new Bundle(CurrentKeyId, nonce, ciphertext, tag), JsonOptions);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public T Decrypt<T>(Guid ownerId, Guid entryId, string field, string json)
    {
        try
        {
            var bundle = JsonSerializer.Deserialize<Bundle>(json, JsonOptions)
                ?? throw new CryptographicException();
            if (bundle.Nonce is not { Length: 12 } || bundle.AuthTag is not { Length: 16 }
                || bundle.Ciphertext is null)
                throw new CryptographicException();
            var plaintext = new byte[bundle.Ciphertext.Length];
            try
            {
                using var aes = new AesGcm(ResolveKey(bundle.KeyId), 16);
                aes.Decrypt(bundle.Nonce, bundle.Ciphertext, bundle.AuthTag, plaintext,
                    AssociatedData(ownerId, entryId, field));
                return JsonSerializer.Deserialize<T>(plaintext, JsonOptions)
                    ?? throw new CryptographicException();
            }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }
        catch (Exception ex) when (ex is JsonException or CryptographicException or ArgumentException)
        {
            // Never attach parsing exceptions: their messages may contain readable content.
            throw new CryptographicException("Entry content could not be decrypted.");
        }
    }

    private sealed record Bundle(string KeyId, byte[] Nonce, byte[] Ciphertext, byte[] AuthTag);
}
