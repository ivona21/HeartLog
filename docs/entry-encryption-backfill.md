# Entry encryption backfill and cutover

## Current production state

On 2026-11-10, read-only inspection of the Neon database configured in the ignored
`HeartLog.Api/.env` found 287 entries: 285 legacy and 2 encrypted. The configured
key authenticated both existing encrypted entries before any backfill write.

At **2026-11-10 09:46:41 UTC**, the backfill completed 285 entries in batches of
100, 100, and 85. Each persisted ciphertext was decrypted and compared to the
original note and `(EmotionId, IsPrimary)` selections before its transaction
committed. Null notes were included. Only encrypted columns were updated, so IDs,
owners, and all three dates were preserved. Plaintext was retained until the
subsequently authorized schema cleanup described below. No original journal entries were
edited through the production API during this operation.

At **2026-11-10 09:47:47 UTC**, the full verification pass authenticated all
287 entries and compared retained originals. It recorded the cutover receipt.
Subsequent edits require another verification immediately before cleanup.
The pre-cutover inspection found **287 encrypted entries, zero legacy-format
entries**, with **283 plaintext notes and 1,275 readable emotion links** retained.

After the user authorized cleanup, all 287 entries were verified again at
**2026-11-10 10:18:24 UTC**. The locally started encrypted-only API applied
`20261110092906_RemoveLegacyEntryStorage` to Neon, dropping the plaintext note
column and the readable entry-to-emotion link table. Its health endpoint passed.
At **2026-11-10 10:19:19 UTC**, post-cleanup verification authenticated all 287
entries with `legacy_schema=False`; inspection also found zero legacy-format
entries. Use 10:19:19 UTC as a conservative upper bound for the last plaintext
removal time when obtaining recovery-history expiry evidence. The one-time
cleanup flag was removed from `.env` afterward. A second API startup without the
flag passed its health check; the temporary local process was stopped to release
port 80 for the IDE. Render deployment status and backup expiry remain unconfirmed.

## Tool

Build from the repository root:

```sh
dotnet build tools/HeartLog.EntryMigration/HeartLog.EntryMigration.csproj
```

The commands below read secrets from the ignored env file without printing them.
For production, this must contain the same v1 key as the API's Render secret file,
not a separate development key. `check-key` authenticates all existing encrypted
entries and refuses to succeed if there are none. `backfill` also authenticates
existing bundles before making changes. Never generate a replacement key for this
operation. Keep the original key in recoverable secure storage.

```sh
dotnet tools/HeartLog.EntryMigration/bin/Debug/net9.0/HeartLog.EntryMigration.dll inspect HeartLog.Api/.env
dotnet tools/HeartLog.EntryMigration/bin/Debug/net9.0/HeartLog.EntryMigration.dll check-key HeartLog.Api/.env
dotnet tools/HeartLog.EntryMigration/bin/Debug/net9.0/HeartLog.EntryMigration.dll backfill HeartLog.Api/.env
dotnet tools/HeartLog.EntryMigration/bin/Debug/net9.0/HeartLog.EntryMigration.dll verify HeartLog.Api/.env
```

Backfill selects `EncryptedEmotions IS NULL`, not missing notes. Null stays null;
empty string stays an encrypted empty string. Emotion IDs and primary flags are
copied without filtering inactive catalog items. Metadata is untouched. Every
batch locks entries and links against writes while permitting ordinary reads;
the two ciphertext bundles are updated atomically. Restart skips committed rows.
A final integer argument limits the number of entries for interruption checks.
Failures log exception type and entry ID, never payloads, key material, or SQL
exception details.

Verification authenticates every encrypted entry and compares retained originals
where legacy values remain. Entries already created/edited by the compatible API
have no original legacy values; those receive authentication/decode checks.
Successful verification writes a temporary `EntryEncryptionCutover` receipt with
a fingerprint of ciphertext and public entry metadata. No plaintext hashes are
stored. A failed verification removes earlier approval. Cleanup checks this
receipt and refuses if encrypted entries or metadata changed. Direct edits to
legacy values alone are outside this fingerprint: stop all old writers and
administrative data changes before final verification.

## Deployment order and future deployments

The database cleanup below has now completed through local API startup. Deploy
this encrypted-only version to any hosted API still using the database; older
versions reference removed storage. The sequence remains documented for audit
and any other database requiring migration.

1. Stop/drain **all** old API instances and any other journal writers. Keep the
   API unavailable during this brief cutover; do not run overlapping old/new
   versions around schema removal. The repository currently has no Render control
   credentials, so this step requires access to the hosting account.
2. Rerun `backfill`, `verify`, and `inspect`. Require zero legacy entries and a
   successful full verification immediately before deployment.
3. Deploy this encrypted-only API with the existing production secret file and
   `EntryEncryption__AllowLegacyCleanup=true`. Its startup migration validates the
   verification receipt, requires encrypted emotions for every row, drops
   `EmotionEntryEmotions` and `EmotionEntries.Comment`, and makes
   `EncryptedEmotions` non-nullable. Startup refuses the destructive migration
   without the explicit flag. Never apply this cleanup while old API instances
   still run. Remove the flag after successful cutover.
4. Rerun `inspect` and `verify`: zero legacy entries, `legacy_schema=False`, and
   all entries decryptable. Confirm the link table is absent. Keep the emotion
   catalog; encrypted selections still resolve catalog IDs.
5. Using an authorized test account, GET `/api/emotion-entries`, PUT a designated
   migrated test entry at `/api/emotion-entries/{id}`, then GET again. Check null
   notes, changed selections and primary flag, ownership, and dates. Do not edit
   arbitrary people's entries to demonstrate verification.
6. Record the schema cleanup UTC timestamp, deployment identifier, API smoke-test
   result, and backup retention evidence below.

Cleanup is intentionally irreversible: a Down migration cannot recover removed
plaintext. After cleanup, rollback only to a version that reads encrypted fields
and does not reference the removed columns or table. The pre-cutover API is safe
only before schema removal.

## Validation performed locally

The integration runner uses a disposable local PostgreSQL instance and real
journal controllers over loopback HTTP with test-only authentication. It never
loads production configuration. It checks interrupted runs, rerun idempotency,
null/empty/Unicode notes, multiple selections, primary flags, inactive selections,
date preservation, comparison failures, missing/stale verification receipts,
actual schema removal, GET reads and PUT edits after cleanup.

```sh
# Start a disposable instance; this name/port must be unused.
docker run --detach --name heartlog-entry-migration-test --publish 127.0.0.1:55439:5432 --env POSTGRES_PASSWORD=local-migration-test postgres:17
dotnet build tests/HeartLog.EntryMigrationChecks/HeartLog.EntryMigrationChecks.csproj
dotnet tests/HeartLog.EntryMigrationChecks/bin/Debug/net9.0/HeartLog.EntryMigrationChecks.dll
```

The runner creates a unique database per run in that disposable container.
Remove that container after testing; it contains only synthetic fixtures.

## Backup retention record — pending account access

Do not infer expiry from a generic plan default. Obtain the actual Neon recovery
window, branches, snapshots, and external export inventory from the account.
Record UTC dates and supporting account evidence. A branch retaining plaintext
has no assumed expiration; it must be cleaned or explicitly deleted. Do not
create new plaintext exports as migration evidence.

| Copy/location | Actual retention | Last plaintext timestamp | Confirmed expiry/deletion UTC | Evidence/status |
| --- | --- | --- | --- | --- |
| Neon recovery history for active branch | Unknown | No later than 2026-11-10 10:19:19 UTC | Unknown | Requires Neon account settings |
| Other Neon branches/snapshots | Inventory pending | Unknown | Unknown | Requires Neon account inventory |
| External database dumps/exports | Inventory pending | Unknown | Unknown | Requires operator inventory |

Production schema cleanup: **completed; verified 2026-11-10 10:19:19 UTC**.
Hosted Render API deployment: **not performed or verified by this session**.
Production journal API smoke test: **pending authorized test account/deployment**.
Backup expiry dates: **unconfirmed; no expiry claim made**.
