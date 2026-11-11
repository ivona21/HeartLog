# Entry encryption

New entries and edits encrypt notes and selected emotion IDs (including the primary flag) with AES-256-GCM. The nullable `EncryptedNote` and `EncryptedEmotions` columns are PostgreSQL `jsonb`; each bundle contains `keyId`, `nonce`, `ciphertext`, and `authTag`. Binary values use Base64. Each encryption uses a random 12-byte nonce and a 16-byte tag. Associated data is UTF-8 `heartlog:entry:{ownerId:D}:{entryId:D}:{field}`, with field `note` or `emotions`.

The entry GUID is allocated before encryption. IDs, ownership, and dates remain queryable. The API still uses emotion keys and `comment`, including a null comment. `PUT /api/emotion-entries/{entryId}` accepts the same body as POST and returns the same response shape. PUT replaces the selected emotions and note; an omitted/null comment clears the note, while an omitted/null occurredAt retains the existing date.

Reads filter ownership before decryption. Presence of `EncryptedEmotions` marks an encrypted row. A null encrypted note is valid; malformed bundles, unknown key IDs, and authentication failures raise errors without falling back to readable storage. The encrypted-only release requires backfill before cutover and removes the legacy mappings. See [the backfill runbook](entry-encryption-backfill.md) for the completed backfill, guarded schema cleanup, deployment order, and outstanding backup retention record.

## Configuration to perform yourself

1. Generate a cryptographically random **32-byte** key and encode it as Base64 using a trusted local secret tool (for example, run `openssl rand -base64 32` privately). Keep a recoverable secure backup; encrypted data requires the original key. Use separate keys for development and production. Never commit the values or put them in logs or tickets.
2. For development, add the following line to the existing ignored `HeartLog.Api/.env` file (run the API from that directory so the existing DotNetEnv loader finds it):

   ```dotenv
   EntryEncryption__Keys__v1=<BASE64_ENCODED_32_BYTE_DEVELOPMENT_KEY>
   ```

   Alternatively, the ignored `HeartLog.Api/appsettings.Development.json` can contain:

   ```json
   { "EntryEncryption": { "Keys": { "v1": "<BASE64_ENCODED_32_BYTE_DEVELOPMENT_KEY>" } } }
   ```

   Merge this section into existing local configuration. Development means `ASPNETCORE_ENVIRONMENT=Development`.
3. Before any future deployment, create a Render Secret File named `heartlog-entry-v1` with content `<BASE64_ENCODED_32_BYTE_PRODUCTION_KEY>`. Render must mount it at `/etc/secrets/heartlog-entry-v1`. In non-development environments the application reads that exact file; there is no environment-variable fallback. Missing, malformed, or incorrectly sized keys stop startup before database migration/seeding. The configured key resolves bundle ID `v1`; unknown IDs fail reads.
4. The additive encryption migration precedes backfill. The cleanup deployment requires `EntryEncryption__AllowLegacyCleanup=true` and a successful verification receipt; stop all old API instances before that deployment. Coordinate key provisioning before that deployment. Production deployment, production backfill, key rotation, and removal of legacy storage are separate work. Do not remove the key while any encrypted rows need it.
