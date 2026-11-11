# Entry encryption

Journal notes and selected emotion IDs, including primary flags, use
AES-256-GCM. `EmotionEntries.EncryptedEmotions` is required PostgreSQL `jsonb`;
`EncryptedNote` is nullable. A null note stays null, while an empty string has an
encrypted payload. The legacy `Comment` column and readable per-entry emotion
links have been removed from the active Neon database.

Each bundle contains `keyId`, `nonce`, `ciphertext`, and `authTag`, with binary
values encoded as Base64. Encryption uses a fresh random 12-byte nonce and a
16-byte authentication tag. Associated data is UTF-8
`heartlog:entry:{ownerId:D}:{entryId:D}:{field}`, where field is `note` or
`emotions`. The GUID is allocated before encryption. IDs, ownership, and dates
remain queryable. The shared `StoredEmotion` contract stores IDs and primary flags.

Ownership is checked before decryption. Malformed bundles, unknown key IDs, and
authentication failures raise errors; there is no plaintext fallback. Lists resolve
decrypted emotion IDs with one bulk catalog query per request, including inactive
historical selections. The catalog remains readable, without per-entry links.

The API contract still uses emotion keys and `comment`. `PUT /api/emotion-entries/{entryId}` accepts the POST body shape and replaces notes and
selections. A null/omitted comment clears the note; null/omitted `occurredAt`
retains the existing occurrence date.

## Keys and local configuration

For a **new, separate database**, generate a cryptographically random 32-byte key
and encode it as Base64 using a trusted secret tool, such as
`openssl rand -base64 32`. Never generate a replacement v1 key for an existing
encrypted database. Database backups require the original key to remain readable;
keep a secure, recoverable key backup and never commit or log it.

In Development, configure the key through the ignored `HeartLog.Api/.env`:

```dotenv
EntryEncryption__Keys__v1=<BASE64_ENCODED_32_BYTE_KEY>
```

The ignored `appsettings.Development.json` can alternatively supply
`EntryEncryption:Keys:v1`. Run the API from `HeartLog.Api` so its DotNetEnv loader
finds `.env`. The key must match the connected database: Development mode does
not make a Neon connection a separate development database. Use separate keys
only when using separate databases.

## Hosted configuration

Non-development startup reads the existing Render Secret File
`/etc/secrets/heartlog-entry-v1`; it has no environment-variable key fallback.
Provision that file with the correct Base64 key before startup. Missing or invalid
keys stop startup before migrations and seeding. Bundles currently use key ID
`v1`; unknown IDs fail reads. Key rotation needs a separate migration design.

## Migration and verification

The production backfill and schema cleanup completed on 2026-11-10. See the
[backfill record and runbook](entry-encryption-backfill.md) for counts, verification,
cleanup timestamps, tools, and unresolved backup-retention tracking. Schema removal
does not establish that recovery history, branches, or exports have expired.

For another database still awaiting cleanup, stop old API instances, backfill with
the correct key, and run full verification before starting this API. Startup
applies pending migrations automatically. The cleanup migration requires a
verification receipt for nonempty databases and rejects incomplete backfills or
changed ciphertext/metadata. There is no separate startup cleanup flag. Older API
versions referencing removed storage cannot be used after cleanup.
