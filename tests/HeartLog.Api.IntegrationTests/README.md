Run from the backend repository root with the SDK pinned in `global.json`.

The default suite needs no database:

```sh
dotnet test HeartLog.sln
```

The sixteen integration tests skip unless `HEARTLOG_TEST_POSTGRES` is supplied.
To run them with a disposable local PostgreSQL 17 container:

```sh
docker run --detach --name heartlog-journal-integration-test \
  --publish 127.0.0.1:55440:5432 \
  --env POSTGRES_PASSWORD=local-journal-test postgres:17

# Wait until this reports "accepting connections" before running the tests.
docker exec heartlog-journal-integration-test pg_isready -U postgres

HEARTLOG_TEST_POSTGRES='Host=127.0.0.1;Port=55440;Database=postgres;Username=postgres;Password=local-journal-test' \
  dotnet test HeartLog.sln

docker rm --force heartlog-journal-integration-test
```

For only the database suite, use the same environment variable with:

```sh
dotnet test tests/HeartLog.Api.IntegrationTests/HeartLog.Api.IntegrationTests.csproj
```

The supplied connection must use `localhost`, `127.0.0.1`, or `::1`, database
`postgres`, and a role with CREATEDB permission. Each test creates a uniquely
named `heartlog_integration_*` database, applies the existing EF migrations, and
drops that database on disposal, including after an assertion fails. The supplied
`postgres` database is used only to create and drop these test databases. No
production `.env`, connection fallback, external identity provider, or email
service is used. An interrupted/killed test process may leave its generated
database behind; removing the disposable container removes that data too.

The test host composes the real `EmotionEntriesController`, `CurrentUserService`,
`EmotionEntryService`, EF repositories, and `ExceptionHandlingMiddleware`.
Only authentication is replaced with a test header containing a synthetic subject.
The real current-user lookup resolves it against the two seeded users. This
exercises HTTP routing, authorization, DTO binding, service behavior, persistence,
and response/error serialization, but does not execute `Program.cs`, Supabase JWT
validation, startup workers, or the production emotion catalog seeder.

Coverage:

- POST and GET `/api/emotion-entries`: note plus selections, selections without
  a note, primary flags, owner isolation, raw JSONB bundle contracts and SQL NULL.
- PUT and DELETE `/api/emotion-entries/{entryId}`: encrypted edits, denied edits
  and deletes by another user, and successful owner deletion.
- Raw PostgreSQL rows/schema: no readable note, emotion keys/selected IDs, or
  encryption key in entry rows; no legacy Comment column or per-entry emotion
  link table after cutover.
- Fixed v1 fixture: linked from the unit project without copying or regenerating
  the fixture; read through the normal API with matching synthetic catalog IDs.
- One damaged ciphertext: generic 500 `unexpectedError`, no partial entry list,
  and no note, selected keys, encryption key, or ciphertext in the response or
  captured text logs (including exception details and structured values/scopes).
  The other user's list remains available.
- Real migration CLI: start at AddEntryEncryption, seed synthetic plaintext and
  links, run backfill twice, verify unchanged bundles on rerun, run verify to
  authorize cutover, apply remaining migrations, verify again, and read through
  the API. A temporary test-only env file is deleted after each CLI invocation.

Current behavior to review: one unreadable entry fails its owner's entire list
with a generic 500 rather than omitting that entry. Backfill retains the legacy
plaintext and links until the verified cutover migration removes them. Backfill
reruns are supported before cutover; the `verify` operation also works afterward.

Additional regression coverage uses the same host and synthetic users:

- List ordering by OccurredAt descending, then CreatedAt descending; empty lists
  and owner-scoped summary counts/latest dates, including an empty summary.
- Invalid create/update requests, missing writes, repeated deletion, removal from
  storage and subsequent reads, and 401 on every supported route without sign-in.
- Edits preserve CreatedAt and preserve OccurredAt when omitted; an explicit
  replacement OccurredAt persists through a fresh HTTP read.
- BLL unit tests cover selection validation for create and update (empty, blank,
  duplicate after trimming, missing/unselected primary, unknown/inactive and
  case-sensitive keys), normalization, optional/verbatim notes, default dates,
  missing records, and empty authenticated IDs without writes.

There is no detail GET endpoint, filtering, or pagination. Reads are the complete
owner-scoped list and summary. Notes have no length/content validator; null, empty,
whitespace, and Unicode/multiline values are accepted. Existing storage tests cover
successful create/edit, encrypted field round-trips, and rejected cross-user writes
followed by an unchanged owner read; the regression tests reuse that coverage.

Authentication and account lifecycle regression coverage is described in
[AUTHENTICATION.md](AUTHENTICATION.md). Those tests also compose the real auth
controller, user service, and Supabase adapter. A loopback-only scripted provider
substitutes Supabase HTTP, including SDK requests; completion-email delivery is
captured at IEmailService. No production provider or email credentials are used.
