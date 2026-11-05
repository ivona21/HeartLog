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

A hosted worker scans up to 20 due tasks every 15 seconds. Each task deletes the Supabase Auth user first, then deletes the matching local user and queue record and inserts a completion-email notification in the same database transaction. Existing foreign keys cascade from the user to emotion entries and their selected-emotion rows. Shared emotions, translations, and unrelated Items remain intact.

Provider failures persist retry metadata with exponential backoff, capped at one hour. Database failures roll back the local transaction, leaving the task available for the next poll. A provider account already absent (`404`) counts as successful deletion, allowing recovery after timeouts or a crash between provider and database cleanup. Shutdown cancellation leaves unfinished work durable.

PostgreSQL transaction advisory locks serialize login provisioning, deletion requests, and cleanup by provider identity across application instances. Login revalidates the provider session under that lock before creating or linking a local user. This prevents a login response obtained before provider deletion from recreating the account afterward. Network calls hold that transaction open; provider request timeouts bound the wait. Hash collisions can serialize unrelated accounts without mixing their data.

JWT authentication resolves local account state for authorized requests. Missing and pending users are rejected even if their JWT has not expired. Only the deletion action permits the pending-user path for idempotent retries; its BLL method still resolves and verifies local ownership. Login and refresh also reject pending accounts.

## Deployment and operations

- Configure backend-only `Supabase__SecretKey` with a Supabase secret key or legacy service-role JWT. Startup validates that it is present. Never expose this key to the frontend. Secret keys use `apikey`; legacy service-role JWTs additionally use the bearer header.
- The existing startup migration mechanism applies the new migration. No production database migration or real account deletion was performed during development.
- Run the backend continuously so queued tasks progress. Monitor warning/error logs and outstanding queue age/attempt counts. Tasks continue retrying rather than silently abandoning personal data. Fix invalid credentials, provider outages, or storage ownership blockers before expecting completion.
- This repository has no user file-storage integration. If storage objects or other user-owned resources are introduced, include their cleanup before deleting the provider account. Supabase-owned storage objects can block Auth deletion.
- This workflow deletes live application records and the provider account. Existing provider audit logs, application logs, exports, and backups follow their own retention policies. Establish those policies separately; restoring backups requires reconciliation of deletion requests to avoid resurrecting deleted accounts.
- Once complete, no deletion task or user profile/journal data is retained. The email outbox temporarily retains the recipient address solely for the completion notification; see below. A later registration with a new provider identity is a new account.

Provider behavior: [Supabase user management](https://supabase.com/docs/guides/auth/managing-user-data), [API key migration](https://supabase.com/docs/guides/getting-started/migrating-to-new-api-keys).

## Verification

A PostgreSQL/Supabase staging test is still required before release to verify applied cascades, transaction rollback, worker restart recovery, concurrent provisioning/deletion, and actual provider credential permissions.

## Deletion completion email

The `EmailOutbox` table holds a notification inserted atomically with local account deletion. The email worker only sees committed rows, so no completion message can be sent before local deletion commits. If deletion rolls back, the notification also rolls back. If the user row is already absent, cleanup completes without inventing a recipient address.

Email failures never restore an account or delay deletion. The API contract is unchanged. A later registration is independent of any remaining notification.

### Configuration

Delivery uses Resend through the BLL `IEmailService` interface and is disabled by default. Set these backend environment variables and restart the application:

```text
Email__Enabled=true
Email__ApiKey=<Resend sending API key>
Email__FromAddress=HeartLog <notifications@your-verified-domain.example>
```

Use a sender domain verified in your Resend account. Never commit the API key. Enabled delivery validates that an API key and syntactically valid sender are configured at startup. Disabled delivery logs a warning and still purges expired outbox rows. Supabase authentication emails keep their existing configuration.

The existing migration-at-startup mechanism creates the table. No new API endpoint or frontend request is required. Configure delivery before deploying if users should receive completion emails immediately.

### Processing and retention

- A hosted worker polls every 15 seconds and processes up to 20 messages per batch. Each message gets a two-minute database lease, claimed using PostgreSQL `FOR UPDATE SKIP LOCKED`, so multiple instances do not normally send the same message concurrently. HTTP sends time out after 30 seconds; no database transaction is held while sending.
- On provider acceptance, the worker deletes the outbox row. Acceptance does not guarantee inbox delivery. Delivery/bounce webhooks are outside this implementation.
- Temporary failures, including network timeouts, rate limits, and provider credential failures, retry with exponential backoff up to one hour. A longer provider `Retry-After` is respected within the retention window. HTTP 400/422 and unknown template versions are treated as permanent rejections and removed with a warning. Shutdown leaves the lease recoverable by another worker after expiry.
- Notifications store only a random message ID, recipient address, template version, timestamps, attempts, and lease metadata. They have no foreign key or retained link to the deleted user. Recipient addresses and provider response bodies are not logged by the delivery implementation.
- Unsent rows expire seven days after deletion and are purged on the next worker cycle, even when sending is disabled. Cleanup resumes on restart if the backend was stopped; a continuously running worker is required for timely expiry. Successfully sent or permanently rejected rows are removed immediately. Provider email logs and database backups have their own retention policies.
- A stable message ID is sent as the provider idempotency key. Resend retains these keys for 24 hours. A crash after acceptance but before row deletion normally retries safely within that window; longer outages can produce a duplicate. New messages use template version `account-deleted-v2` with the custom responsive HTML and matching plain text. Only `account-deleted-v2` is supported; any older template versions are handled as permanent rejections. Keep sender configuration stable while retrying pending messages because the provider rejects reuse of a key with a different payload.

### Email content

Subject: **Your HeartLog account has been deleted**

> Your HeartLog account and emotion entries have been permanently deleted from our active systems.
>
> You don’t need to take any further action. If you ever want to come back, you’re welcome to start fresh with a new account.
>
> Thank you for using HeartLog.

Both plain-text and HTML bodies are sent. The wording covers active account data without claiming historical backups or provider logs were erased.

Provider references: [sending emails](https://resend.com/docs/api-reference/emails/send-email), [idempotency keys](https://resend.com/docs/dashboard/emails/idempotency-keys).

Before release, verify the sender configuration in staging, transaction rollback after outbox insertion, multi-instance claims, crash recovery, retry/expiry behavior, and actual provider acceptance. No real email was sent during implementation, and no test project was added to this repository.
