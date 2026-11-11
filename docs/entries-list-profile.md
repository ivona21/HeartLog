# Warm entries-list profile

Measured 2026-11-10 against Neon using the same existing account, with 282 entries
and 1,225 selected emotions. No account/entry identifiers, note contents, selected
emotion identifiers, SQL text, SQL parameters, or plaintext hashes were logged.
All database sessions used `default_transaction_read_only=on`.

## Result

Release build, one excluded warm-up request and three measured requests per
version. Each request uses a fresh request-scoped DbContext and a warmed shared
connection pool. These are real HTTP GETs through the journal controller, mapper,
service, and EF repositories in a local loopback diagnostic host. A test-only
identity adapter selects the same account without issuing user/authentication
queries. Consequently, timings and command counts below describe the journal
list path and local HTTP overhead; hosted network latency, JWT validation, and
production authentication/current-user SQL are excluded. No deployment is claimed.

| Warm median | Before | After |
| --- | ---: | ---: |
| EF entries fetch, including materialization | 318.608 ms | 161.344 ms |
| AES-GCM decryption path, including JSON decoding | 68.127 ms | 7.543 ms |
| Emotion lookup and dictionary construction | 20,192.900 ms | 107.018 ms |
| HTTP request, including response serialization/read | 20,740.782 ms | 283.882 ms |
| SQL commands per request | 283 | 2 |
| Entry fetch commands | 1 | 1 |
| Emotion lookup commands | 282 | 1 |

Request median improved about 73 times (98.6% less time). Component medians are
computed independently and need not sum to the request median. AES code was not
changed: its observed timing difference includes scheduling/JIT/runtime effects;
the confirmed and addressed bottleneck was repeated Neon round trips.

Measured warm samples (timings in milliseconds):

| Version / sample | Fetch | Decrypt | Emotion lookup | Request | SQL count |
| --- | ---: | ---: | ---: | ---: | ---: |
| Before / 1 | 318.608 | 68.127 | 19,859.055 | 20,267.354 | 283 |
| Before / 2 | 437.878 | 86.491 | 20,192.900 | 20,740.782 | 283 |
| Before / 3 | 279.264 | 62.147 | 20,910.838 | 21,268.493 | 283 |
| After / 1 | 202.445 | 7.543 | 216.103 | 430.450 | 2 |
| After / 2 | 139.529 | 5.131 | 64.013 | 213.067 | 2 |
| After / 3 | 161.344 | 12.639 | 107.018 | 283.882 | 2 |

## Change

The old list called `ToResultAsync` per entry, which issued a separate
`GetByIdsAsync` query each time. The new list decrypts each entry, collects distinct
emotion IDs across the response, resolves them in one query, and maps all entries
using that request's dictionary. No cross-request cache is introduced. Historical
inactive emotions remain resolvable, notes remain nullable, primary flags and
ordering are unchanged, and the bulk query accepts request cancellation.
An empty account skips the lookup and issues just the entry fetch.

`HeartLog.EntryReads` provides opt-in activity durations for `entries.fetch`,
`entries.decrypt`, and `entries.emotions`. Activities have no content tags, and
no profiling listener or logger is enabled by the normal API. Fetch spans include
EF query execution and materialization. Decrypt spans include payload parsing,
AES-GCM authentication/decryption, and JSON decoding. Lookup spans include EF
execution, materialization, and dictionary creation. The profiling host counts
all EF command executions and classifies them by the current activity, without
inspecting or logging SQL text.

## Correctness

All 282 response entries and 1,225 selections were independently checked against
direct payload decryption and the emotion catalog before and after. Notes,
primary flags, entry IDs, dates, selections, and order also matched the complete
before response exactly. The comparison artifact contained only an encrypted
response and account reference, was stored outside the repository, and was
deleted after the successful comparison. Production data was not modified.

Temporary disposable-PostgreSQL checks verified two commands for a populated
journal and one for an empty account, alongside null/empty/Unicode notes, primary
flags, inactive emotions, and normal HTTP reads and edits. These checks passed
during implementation; the temporary test project was subsequently removed
pending a dedicated test-suite task. The profiling tool remains available.

## Reproduce

The profiling tool is separate from normal API startup and does not run migrations
or hosted workers. Run `before` with the original per-entry lookup implementation,
then rebuild after changing it and run `after` using the same encrypted artifact.
`after` checks output equality, independently verifies decrypted values, asserts
the optimized query budget, and deletes the comparison artifact. Runtime logs
contain only numeric timings and counts; errors are reported as `error_count=1`.

```sh
dotnet build tools/HeartLog.EntryProfile/HeartLog.EntryProfile.csproj -c Release
dotnet tools/HeartLog.EntryProfile/bin/Release/net9.0/HeartLog.EntryProfile.dll before HeartLog.Api/.env /private/tmp/heartlog-entry-profile-reference.enc
# Apply the bulk-lookup change and rebuild the same Release project.
dotnet tools/HeartLog.EntryProfile/bin/Release/net9.0/HeartLog.EntryProfile.dll after HeartLog.Api/.env /private/tmp/heartlog-entry-profile-reference.enc
```
