# Agreement flow (under development)

`GET /api/agreements/requirements` is public and returns the current version,
URL, and checkbox statement for each requirement. The frontend must display the
matching bundled documents and submit those exact versions. Published source
snapshots are in `docs/legal/snapshots`; the consistency check is documented by
running `sh tools/check-legal-snapshot.sh <frontend-legal-directory>`.

`POST /api/auth/register` requires an `agreements` object with `termsAccepted`,
`privacyPolicyAcknowledged`, `journalProcessingConsent`, and the corresponding
`termsVersion`, `privacyPolicyVersion`, and `journalProcessingVersion` strings.
Each checkbox must be checked and all versions must be current. The server stores
an expiring, hashed registration intent and sends its token in Supabase signup
metadata. On login it retrieves metadata using the access token and requires the
same provider user ID returned at signup. Consumption and event inserts share a
transaction; expired, missing, copied, or consumed intents grant nothing.

Authenticated clients can fetch `GET /api/agreements/me` to see each current
version, the last recorded version and action, and `actionRequired`. To submit
an authenticated decision, call `POST /api/agreements/me` with a unique
`submissionId` UUID and an `items` array containing `key` and `version`. Each
item represents an explicit user action. A retry with the same submission ID and
same items is accepted; reuse with different items conflicts. Existing users with
no records must be prompted. Journal endpoints return 403 with
`journal_consent_required` without a current grant.

Do not deploy this flow yet. The unconfirmed duplicate-signup behavior could not
be verified in the configured Supabase project because the provider returned HTTP
500 during the live probe. The frontend agreement UI, shared content build
integration, withdrawal, OpenAPI export, and complete failure/race coverage remain
unfinished. Signup metadata is user-readable; the token is therefore also bound
to the provider ID and must never be treated as a secret proof of identity.
