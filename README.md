# HeartLog backend

ASP.NET Core journal API using PostgreSQL/Neon, Supabase authentication, and
application-side encryption for notes and emotion selections.

## Local development

Use the .NET 9 SDK version pinned in [global.json](global.json). Configure the
ignored `HeartLog.Api/.env` with your database connection, Supabase settings, and
the encryption key belonging to that database. Configuration details are in the
[auth guide](docs/frontend-auth-flow.md#backend-and-supabase-configuration) and
[encryption guide](docs/entry-encryption.md). Completion-email configuration is in
[account deletion](docs/account-deletion.md#configuration).

```sh
dotnet build HeartLog.sln
cd HeartLog.Api
dotnet run --launch-profile http
```

Startup applies database migrations, seeds the emotion catalog, and starts
account-deletion and email workers against the configured database. Check the
connection before starting, especially when using Neon. A database awaiting legacy
encryption cleanup requires the explicit cutover described in the encryption guide.

Kestrel currently listens on port 80 despite the launch-profile port. Development
Swagger is at `http://localhost/index.html`; health is at `http://localhost/health`.
Production reads the entry key from a secret file rather than `.env`.

## Maintained guides

- [Authentication and frontend integration](docs/frontend-auth-flow.md): registration, confirmation, sessions, recovery, cookies, and provider configuration.
- [Account deletion](docs/account-deletion.md): API contract, background cleanup, completion emails, and operational requirements.
- [Entry encryption](docs/entry-encryption.md): storage format, key configuration, and cutover requirements.
- [OpenAPI workflow](docs/openapi-generation-workflow.md): exporting the committed [API contract](openapi/heartlog.openapi.json).

## Operational records and tools

- [Encryption backfill and cutover](docs/entry-encryption-backfill.md): completed migration evidence, verification commands, and unresolved backup-expiry inventory.
- [Entries-list profile](docs/entries-list-profile.md): measured before/after timings and profiling instructions.
- `tools/HeartLog.EntryMigration`: explicit backfill, inspection, key checks, and verification.
- `tools/HeartLog.EntryProfile`: explicit read-only profiling with timing/count logs.

Tools do not run during normal API startup. The temporary implementation test
project was removed; a dedicated test suite is planned separately. Generated
`bin/` and `obj/` files and secret configuration remain ignored by Git.
