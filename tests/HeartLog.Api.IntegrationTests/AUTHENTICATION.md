Authentication and account lifecycle tests

Use the commands and disposable PostgreSQL setup in [README.md](README.md).
All integration tests require HEARTLOG_TEST_POSTGRES. The BLL unit suite runs
without PostgreSQL, Supabase, an email server, or environment configuration:

```sh
dotnet test tests/HeartLog.BLL.Tests/HeartLog.BLL.Tests.csproj
```

The HTTP tests use the existing PostgresTestHost, its two synthetic identities,
and a newly migrated database per test. AuthenticationLifecycleTests and
AccountDeletionLifecycleTests run the real controller, UserService,
CurrentUserService, repositories, exception middleware, and SupabaseAuthService.
ScriptedSupabase listens only on an ephemeral loopback port and supplies queued
provider responses; it checks the method, path, request data, and API key. This
also tests the Supabase SDK branches, which construct their own HttpClient.
The email outbox uses the real SQL repository and processor, with IEmailService
substituted to capture deliveries and simulate transient failures. Processors are
invoked explicitly in fresh scopes, avoiding timer sleeps and background races.

| HeartLog path | Covered contract |
| --- | --- |
| POST /api/auth/login | Access token, email and expiry in body; refresh token only in cookie; local provisioning after provider session revalidation; invalid credentials and unconfirmed email return distinct 401 codes; provider outage returns 503 without cookies. |
| POST /api/auth/refresh | Reads refresh cookie, rotates tokens/cookie; absent or rejected token returns 401; unknown/pending local user cannot refresh; missing provider refresh token returns 503. |
| POST /api/auth/logout | Anonymous/idempotent cookie clearing, 200; no Supabase session-revocation request. |
| GET /api/auth/me | Resolves provider subject to local user; anonymous, unresolved and deletion-pending users return 401. |
| POST /api/auth/register | Returns email and confirmation instruction, without cookies or local provisioning; duplicate provider signup triggers resend and returns the same public 200 response; invalid email/password returns 400; provider failure returns 503. |
| POST /api/auth/forgot-password | Provider recovery request; same public 200 for known and unknown emails, including provider 4xx; malformed email returns 400; provider 5xx returns 503. |
| POST /api/auth/forgot-password/me | Uses the authenticated local email; anonymous returns 401. |
| GET /api/auth/confirm-email | Verifies token_hash with type=email, redirects to frontend status=success/invalid/expired; unsupported type is invalid without provider call; missing required query fields return 400; sets no session cookies. |
| POST /api/auth/resend-confirmation | Sends provider type=signup; same public 200 for eligible and unknown/ineligible accounts, including provider 4xx; malformed email returns 400; provider 5xx returns 503. |
| GET /api/auth/reset-password/confirm | Verifies type=recovery; redirects to frontend status=ready/invalid/expired; success sets a recovery access-token cookie; unsupported type and missing query fields are rejected. |
| POST /api/auth/reset-password | Requires recovery cookie and valid password; sends password update followed by global sign-out; clears recovery and refresh cookies on success; absent/rejected token returns 401; weak password returns 400. |
| POST /api/auth/change-password | Requires authenticated local user, bearer token, valid new password and correct current password; updates provider password then revokes other sessions; missing bearer/wrong current password returns 401; weak password returns 400. |
| DELETE /api/auth/me | Requires authentication and current password; rejects wrong password, mismatched provider identity, missing required input and provider verification outage without scheduling deletion; accepts a durable request with 202 and clears both cookies. |

Cookie assertions parse the actual Set-Cookie headers: HttpOnly, Secure,
SameSite=None in the Testing environment, cookie paths and lifetimes (30 days
refresh, 15 minutes recovery). Deletion headers expire an empty cookie at the
same path; ASP.NET omits Max-Age when writing Cookie.Delete. Test clients transfer
cookie values explicitly rather than relying on browser behavior.

The deletion workflow test verifies:

- A repeated pending request is accepted without another password-provider call.
  Current-user and journal reads, login and refresh reject the pending user.
- Supabase deletion failure leaves the local user, journal and durable task
  intact, increments attempts, schedules a future retry, and sends no email.
  Processing before the due time makes no provider call.
- A forced outbox constraint failure after provider success rolls back local user
  and journal deletion and leaves the durable task available for retry.
- Supabase 404 is successful cleanup on retry. Local user and journal rows are
  removed, the task is removed, and one completion notification is committed.
  The other synthetic user's account and journal remain intact.
- Email is attempted only after cleanup. Transient delivery failure keeps the
  notification and schedules a retry; delivery uses the same message ID on retry.
  Successful delivery removes the notification; another processor run does not
  send again. A delete request after local removal returns 401.

Database-free unit tests cover UserService's provisioning, legacy-email linking,
provider-identity mismatch, provider-session revalidation under the lock,
missing/pending users, deletion idempotency and cancellation. CurrentUserService
rejects absent/malformed/unknown/pending subjects. AuthProviderContractTests
replace HttpMessageHandler for non-SDK adapter calls: payloads, token shape and
expiry, invalid tokens, provider status mapping, reset/global-sign-out failure,
admin deletion success/404/failure, and early token/type rejection. They also
exercise ResendEmailService's completion-email payload, stable idempotency key,
and permanent versus retryable delivery errors without contacting Resend.

Boundaries and gaps

- Supabase creates accounts, stores credentials, decides confirmation eligibility,
  verifies hashes and expiration, issues tokens, and sends signup/recovery emails.
  Tests simulate those responses and exercise HeartLog's adapter and HTTP behavior;
  they do not prove live Supabase project configuration or actual email delivery.
- Registration returns only an email and confirmation instruction. HeartLog sets
  no local confirmation flag and creates no local account until successful login.
  Duplicate signup is deliberately not a conflict response.
- The frontend renders the configured confirmation/reset redirects and submits the
  new password with credentials. Browser cookie policy/CORS, frontend screens and
  email-link navigation are outside these tests.
- The existing test authentication scheme supplies synthetic subjects. Production
  Program.cs, ES256 JWT signature/issuer/audience/lifetime validation and its
  OnTokenValidated hook are not executed. Application-level current-user and
  pending-deletion checks are covered, including rejected writes and reads.
- Background timer scheduling, multi-process contention, and email lease expiry
  are not exercised here. The real deletion transactions and outbox retry queries
  are exercised by explicit processor runs. Workers' 15-second polling adds no
  separate business decisions.
- Logout currently clears the refresh cookie only. It does not revoke provider
  sessions. Reset updates the provider password before global sign-out; a
  sign-out outage is surfaced as an error after the password update succeeds.
