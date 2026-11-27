# Legal content and agreement API

`legal-release.json` selects the exact manifest in the checked-out, pinned
`shared/heartlog-legal` submodule. The application verifies the selector,
manifest and content as UTF-8 without BOM, with LF newlines, and checks SHA-256
on the exact file bytes. It also verifies the historic `2026-11-23` snapshots
against their pre-existing hashes. The publish output includes both the shared
content and historical snapshots; runtime never fetches from GitHub.

The current selection is a **draft**, intended only for local integration.
Development verification accepts it; publication verification and a
non-Development application startup reject it. Draft runtime requires
`ASPNETCORE_ENVIRONMENT=Development` in an isolated environment.

## Requests

`GET /api/agreements/requirements` returns current `terms` and `privacyPolicy`
requirements with `version`, `sha256`, `url`, and `statement`. Registration
requires one checkbox reflected as separate decisions:

```json
{
  "email": "user@example.com",
  "password": "StrongPass123!",
  "agreements": {
    "termsAccepted": true,
    "privacyPolicyAcknowledged": true,
    "termsVersion": "draft-v1",
    "termsHash": "<manifest documents.terms.sha256>",
    "privacyPolicyVersion": "draft-v1",
    "privacyPolicyHash": "<manifest documents.privacy.sha256>"
  }
}
```

The values must identify the content the frontend displayed. A missing, unknown
or stale version or hash returns HTTP 409 with `code: legal_content_outdated`.
False decisions return HTTP 400. The registration intent stores the submitted
versions and hashes; after provider identity verification two separate events
record them.

`GET /api/agreements/journal-consent/statement` provides `consentId`,
`consentVersion`, `contentHash`, `checkboxLabel`, `statement`, `explanation`,
and `withdrawal`. The frontend may bundle these values but must show the entire
statement and explanation before obtaining consent. On the first save attempt,
after the user checks the box, submit to `POST /api/agreements/journal-consent`:

```json
{
  "submissionId": "f79f2881-3959-4147-920c-940c9098c683",
  "journalProcessingConsent": true,
  "consentId": "saved-entries-sensitive-data",
  "consentVersion": "draft-v1",
  "contentHash": "<manifest consents.savedEntries.sha256>",
  "statement": "<the exact manifest consent statement shown to the user>"
}
```

The consent version and hash are evidence identifiers, not a separate document
for the user to accept. A stale value or statement returns HTTP 409 with
`code: legal_content_outdated`; show the new content and obtain a fresh decision.
An identical retry with the same submission ID returns 204; reuse for different
data returns `409 submission_id_reused`. After a grant, refresh
`GET /api/auth/me`, then retry saving the preserved draft. Journal endpoints
return `403 journal_consent_required` when no grant is active.

Authenticated users can review `GET /api/agreements/me` and submit Terms and
privacy decisions to `POST /api/agreements/me`, using a unique `submissionId`
and `items` containing `key`, `version`, and `contentHash`. Old versions and
hashes are not accepted for new decisions. Existing consent events remain in
force regardless of later wording changes; this release does not implement
withdrawal and does not silently withdraw or re-grant consent.

## History and rollout

The migration adds nullable evidence fields only. Existing events and pending
registration intents retain their old IDs, versions, text and null hashes; no
hash or consent is invented for them. Historic Terms and privacy versions and
the old consent statement remain distinct from the shared draft files. The
requirements endpoint may indicate renewed action for a user whose historical
acceptance does not identify the selected current content.

By default, older clients that omit hashes or send `2026-11-23` receive 409
`legal_content_outdated` for new decisions. For a controlled transition only,
`Legal__AllowLegacyClients=true` additionally accepts the exact historic
`2026-11-23` Terms and Privacy Policy versions without hashes, and the exact
historic journal statement without consent identifier, version or hash. No
missing evidence is invented. Other old or unknown inputs still receive 409.
Legacy decisions are recorded as legacy decisions and may remain marked as
requiring action for the selected current documents. Already-granted consent and
authenticated journal access continue to work regardless of that flag.

Before rollout, build a frontend that bundles the selected release, displays
the full consent content, sends these fields, and handles 409 by prompting a
refresh. Coordinate its release with the backend; if legacy clients must remain
supported during the transition, enable the flag for a limited, reviewed period.
Monitor and retire old bundles, then remove the flag. A new frontend must not
silently downgrade to the old API while displaying new text. The current draft
cannot be deployed as a production release. Test the coordinated rollout in an
isolated environment first.

The unconfirmed duplicate-signup behavior remains unverified in the configured
Supabase project because its live probe returned HTTP 500. Frontend integration,
shared-content build integration, withdrawal, and additional failure/race tests
remain unfinished. Do not deploy until those items and publication review are
complete.

## Build and hosting

Initialise the recorded commit with `git submodule update --init --recursive`.
In GitHub Actions configure `HEARTLOG_LEGAL_READ_TOKEN` as a read-only token
with access to both the app and private legal repositories. PRs from forks do
not receive repository secrets, so require a trusted same-repository workflow
or an appropriately scoped GitHub App workflow for those checks.

For Git-backed Render builds, grant the connected Render Git provider credential
access to both repositories. Render checks out `.gitmodules` with those
credentials before building; Docker itself neither fetches Git nor stores a key.
The existing `render.yaml` should be reviewed separately: its indentation and
repository/connection settings predate this integration and are not reliable as
a deployable blueprint. Docker builds default to production legal validation.
