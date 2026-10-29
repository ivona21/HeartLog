# Account deletion

`DELETE /api/auth/me` permanently deletes the authenticated user's account and emotion data. It uses the existing API → BLL → DAL/provider architecture.

## Frontend contract

Send the access token in the `Authorization: Bearer ...` header, include credentials, and supply JSON:

```json
{ "currentPassword": "the user's current password" }
```

A successful request returns HTTP `202`:

```json
{ "success": true, "message": "Account deletion requested." }
```

Acceptance is durable; it does not mean external cleanup has already finished. Access is blocked immediately. The response expires both existing auth cookies using their original paths. Clear the frontend access token, persisted user data, journal caches, and summaries, then show a signed-out confirmation that permanent deletion is processing.

The frontend should require explicit confirmation before sending the request. Incorrect passwords return the existing `401 / invalidCurrentPassword` error; authentication provider failures before acceptance return `503 / authenticationUnavailable`. Invalid or missing request data returns `400`. Pending accounts can repeat deletion requests without creating another task. Once cleanup completes, their old tokens return `401`.

## Persistence and processing

The migration adds `Users.DeletionRequestedAt` and `AccountDeletions`. The queue stores only identifiers, request time, attempts, and next attempt time. It deliberately has no user foreign key, so the retry record survives independently of local user cleanup.

Password verification must return the same Supabase identity as the authenticated caller. A PostgreSQL transaction atomically marks the account and records the task. Once committed, request cancellation cannot discard the task.

A hosted worker scans up to 20 due tasks every 15 seconds. Each task deletes the Supabase Auth user first, then deletes the matching local user and queue record in a database transaction. Existing foreign keys cascade from the user to emotion entries and their selected-emotion rows. Shared emotions, translations, and unrelated Items remain intact.

Provider failures persist retry metadata with exponential backoff, capped at one hour. Database failures roll back the local transaction, leaving the task available for the next poll. A provider account already absent (`404`) counts as successful deletion, allowing recovery after timeouts or a crash between provider and database cleanup. Shutdown cancellation leaves unfinished work durable.

PostgreSQL transaction advisory locks serialize login provisioning, deletion requests, and cleanup by provider identity across application instances. Login revalidates the provider session under that lock before creating or linking a local user. This prevents a login response obtained before provider deletion from recreating the account afterward. Network calls hold that transaction open; provider request timeouts bound the wait. Hash collisions can serialize unrelated accounts without mixing their data.

JWT authentication resolves local account state for authorized requests. Missing and pending users are rejected even if their JWT has not expired. Only the deletion action permits the pending-user path for idempotent retries; its BLL method still resolves and verifies local ownership. Login and refresh also reject pending accounts.

## Deployment and operations

- Configure backend-only `Supabase__SecretKey` with a Supabase secret key or legacy service-role JWT. Startup validates that it is present. Never expose this key to the frontend. Secret keys use `apikey`; legacy service-role JWTs additionally use the bearer header.
- The existing startup migration mechanism applies the new migration. No production database migration or real account deletion was performed during development.
- Run the backend continuously so queued tasks progress. Monitor warning/error logs and outstanding queue age/attempt counts. Tasks continue retrying rather than silently abandoning personal data. Fix invalid credentials, provider outages, or storage ownership blockers before expecting completion.
- This repository has no user file-storage integration. If storage objects or other user-owned resources are introduced, include their cleanup before deleting the provider account. Supabase-owned storage objects can block Auth deletion.
- This workflow deletes live application records and the provider account. Existing provider audit logs, application logs, exports, and backups follow their own retention policies. Establish those policies separately; restoring backups requires reconciliation of deletion requests to avoid resurrecting deleted accounts.
- Once complete, no deletion task or local user data is retained. A later registration with a new provider identity is a new account.

Provider behavior: [Supabase user management](https://supabase.com/docs/guides/auth/managing-user-data), [API key migration](https://supabase.com/docs/guides/getting-started/migrating-to-new-api-keys).

## Verification

Run the dependency-free regression executable:

```bash
dotnet run --project tests/HeartLog.AccountDeletionChecks
```

It checks password and identity verification, duplicate acceptance, pending access/login/refresh rejection, retry orchestration, stale-login rejection, admin HTTP headers/status handling, EF cascade metadata, migration snapshot consistency and generated SQL, cookie clearing, and the authenticated OpenAPI contract. Provider requests are simulated. It does not contact Supabase or apply migrations to an application database.

A PostgreSQL/Supabase staging test is still required before release to verify applied cascades, transaction rollback, worker restart recovery, concurrent provisioning/deletion, and actual provider credential permissions.
