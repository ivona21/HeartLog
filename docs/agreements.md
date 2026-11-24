# Agreement flow (under development)

`GET /api/agreements/requirements` is public and returns the current version,
URL, and checkbox statement for each requirement. The frontend must display the
matching bundled documents and submit those exact versions. Published source
snapshots are in `docs/legal/snapshots`; the consistency check is documented by
running `sh tools/check-legal-snapshot.sh <frontend-legal-directory>`.

`POST /api/auth/register` requires an `agreements` object with `termsAccepted`,
`privacyPolicyAcknowledged`, `termsVersion`, and `privacyPolicyVersion`. Both
decisions must be true and both versions must be current. The server stores
an expiring, hashed registration intent and sends its token in Supabase signup
metadata. On login it retrieves metadata using the access token and requires the
same provider user ID returned at signup. Consumption and event inserts share a
transaction; expired, missing, copied, or consumed intents grant nothing. It
records only Terms and Privacy Policy decisions, not journal consent.

Authenticated clients can fetch `GET /api/agreements/me` to see each current
version, the last recorded version and action, and `actionRequired`. To submit
an authenticated decision, call `POST /api/agreements/me` with a unique
`submissionId` UUID and an `items` array containing `key` and `version`. Each
item represents an explicit user action. A retry with the same submission ID and
same items is accepted; reuse with different items conflicts. Existing users with
no records must be prompted.

`GET /api/auth/me` returns `data.journalConsent.status`, which is `notGranted`,
`granted`, or `withdrawn`. An absent event is `notGranted`. The frontend should
show the consent checkbox on the first save attempt, preserve the draft if the
user declines, and submit the exact wording displayed:

```json
{
  "submissionId": "f79f2881-3959-4147-920c-940c9098c683",
  "journalProcessingConsent": true,
  "statement": "I explicitly consent to HeartLog processing the emotions and journal entries I choose to save, including information they may reveal about my health, religious beliefs or sexual orientation, so that HeartLog can save and display my entries. I can withdraw this consent at any time in Settings."
}
```

Send this body to `POST /api/agreements/journal-consent`. The backend verifies
the text matches the current statement and stores that exact text, without a
consent document version. The current wording is also available from the public
`GET /api/agreements/journal-consent/statement`. A client may bundle that wording,
but must handle `409 journal_consent_statement_changed` by showing the new
wording and obtaining a fresh decision. `submissionId` makes identical retries
idempotent; reuse for a different decision returns `409 submission_id_reused`.
After `204`, refresh `/api/auth/me` and retry the save. Journal endpoints return
`403 journal_consent_required` without a grant. The frontend can render an empty
state without calling them for users who have not consented.

Do not deploy this flow yet. The unconfirmed duplicate-signup behavior could not
be verified in the configured Supabase project because the provider returned HTTP
500 during the live probe. The frontend agreement UI, shared content build
integration, withdrawal, and complete failure/race coverage remain
unfinished. Signup metadata is user-readable; the token is therefore also bound
to the provider ID and must never be treated as a secret proof of identity.
