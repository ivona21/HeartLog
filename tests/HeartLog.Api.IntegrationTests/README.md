Run from the backend repository root with the SDK pinned in `global.json`.

Run only the database-free unit suite (the CI unit job uses this project):

```sh
dotnet restore HeartLog.sln
dotnet test tests/HeartLog.BLL.Tests/HeartLog.BLL.Tests.csproj --no-restore
```

The full solution also runs without a database, but skips integration cases:

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

## GitHub Actions CI

[Backend tests](../../.github/workflows/backend-tests.yml) runs on every pull
request opened, updated with new commits, reopened, or marked ready for review.
There are no branch or path filters, so backend and shared build changes always
run both checks, including on draft pull requests. Runs for the same PR cancel an
older in-progress run. After the workflow reaches the default branch, it can also
be run manually from the Actions tab using **Run workflow**.

The independent Ubuntu jobs are named **Backend unit tests** and **Backend
PostgreSQL tests**. Both check out the repository root, install the SDK from
`global.json` (currently 9.0.315), restore `HeartLog.sln`, and run their explicit
test project. The PostgreSQL job uses a disposable `postgres:17` service container
with a `pg_isready -U postgres -d postgres` health check; GitHub waits for it to
be healthy before running steps. Its port is bound to runner loopback
`127.0.0.1:55440`. See GitHub's [PostgreSQL service-container guide](https://docs.github.com/en/actions/tutorials/use-containerized-services/create-postgresql-service-containers).

These are the CI commands, also runnable locally from the repository root after
starting the PostgreSQL container above:

```sh
dotnet restore HeartLog.sln

dotnet test tests/HeartLog.BLL.Tests/HeartLog.BLL.Tests.csproj \
  --no-restore --logger 'trx;LogFileName=unit.trx' \
  --results-directory TestResults/unit
python3 .github/scripts/check-test-results.py TestResults/unit/unit.trx

HEARTLOG_TEST_POSTGRES='Host=127.0.0.1;Port=55440;Database=postgres;Username=postgres;Password=local-journal-test' \
  dotnet test tests/HeartLog.Api.IntegrationTests/HeartLog.Api.IntegrationTests.csproj \
  --no-restore --logger 'trx;LogFileName=postgres.trx' \
  --results-directory TestResults/postgres
python3 .github/scripts/check-test-results.py TestResults/postgres/postgres.trx
```

CI uses absolute result directories under `GITHUB_WORKSPACE`; the relative paths
above refer to the same locations when run from the repository root. Python 3 is
needed only for checking the TRX reports. `TestResults/` is ignored by Git.

Configuration is entirely synthetic:

| Setting | Source and value |
| --- | --- |
| `HEARTLOG_TEST_POSTGRES` | Integration job only; the connection string above is also the opt-in switch. No separate boolean switch exists. |
| `POSTGRES_USER`, `POSTGRES_DB` | Service container settings, both `postgres`; this role can create the per-test databases. |
| `POSTGRES_PASSWORD` | Service container setting `local-journal-test`, matching the connection string. This is a disposable test credential. |
| Entry encryption key | Already fixed in `PostgresTestHost.Key`: `AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=`. No CI key variable is read or required. |
| Synthetic users and emotion catalog | Already seeded by `PostgresTestHost` after its existing EF migration setup. |
| Supabase, email, frontend | Existing test doubles and fixture settings, including the loopback scripted provider for auth tests. No host secrets, external provider accounts, or real delivery are required. |

The migration CLI test creates its own temporary env file containing only the
test database connection and fixed key, and deletes it afterward. CI must not
supply the production `.env`, Neon connection, Supabase keys, or email secrets.

`dotnet test` failures fail their job directly. The TRX guard also runs after a
failed test step and rejects missing results, zero cases, any skipped case, or
any case that did not pass. It checks counters against individual result rows;
it does not hardcode a count, so new tests are included automatically. Current
baseline: **89 unit tests and 16 PostgreSQL integration tests**, including journal
storage, entry CRUD, and authentication/account lifecycle. A fully skipped
integration run therefore cannot produce a green check merely because the
opt-in variable was omitted.

Open a pull request's **Checks** tab and select either job, or go to
[Actions → Backend tests](https://github.com/ivona21/HeartLog/actions/workflows/backend-tests.yml),
select the run and then the job. Expand **Run database-free tests** or **Run
PostgreSQL integration tests** for xUnit output and **Verify ... tests executed**
for the counts. TRX reports are uploaded even after test failure as
`backend-unit-test-results` and `backend-postgres-test-results`, retained for
14 days, and downloadable from the run's summary page.

## First hosted run

No Actions secrets are needed. In the repository's **Settings → Actions →
General**, allow GitHub Actions and the official `actions/checkout`,
`actions/setup-dotnet`, and `actions/upload-artifact` actions if an allowlist is
enforced. The workflow requests only `contents: read`; it does not need write
permissions. Fork contributors may need a maintainer to approve their first run
according to the repository's policy.

From the current `feature/tests-ci` branch:

```sh
git add .github/workflows/backend-tests.yml .github/scripts/check-test-results.py \
  .gitignore README.md tests/HeartLog.Api.IntegrationTests/README.md
git commit -m "Add backend unit and PostgreSQL test CI"
git push -u origin feature/tests-ci
```

Open or update the branch's pull request at
[HeartLog pull requests](https://github.com/ivona21/HeartLog/pulls). Confirm both
named checks pass and their verification steps report nonzero executed counts
with zero skips (currently 89 and 16). A later commit to the PR should start a
new run automatically. After a successful first run, optionally require both
check names in the default branch's protection rule or ruleset to block merges
when either suite fails. This is a repository setting; the workflow itself does
not change branch protection.
