# Authentication and frontend integration

This is the maintained authentication guide. The generated
[OpenAPI contract](../openapi/heartlog.openapi.json) describes request schemas and
error responses; [account deletion](account-deletion.md) covers deletion and its
background processing.

## Identity and data ownership

Supabase owns credentials, email confirmation, and access/refresh tokens. The
frontend calls HeartLog's API. HeartLog stores application users and journal data,
linking `Users.SupabaseUserId` to the Supabase identity.

Registration creates a Supabase account without a HeartLog session or local user.
After email confirmation, successful login creates or links the local user. Login
first looks up the provider identity, then may link a matching email with no
provider identity, or create a new local user. An email linked to a different
provider identity is rejected. Pending account deletion blocks login, refresh,
and protected access.

Protected requests use `Authorization: Bearer ACCESS_TOKEN`. The backend validates
the token and resolves the local user from its `sub` claim. Clients must not send
user IDs for ordinary user-owned operations or decode tokens to infer the local
HeartLog identity; use `GET /api/auth/me`.

## Registration and email confirmation

### Register

`POST /api/auth/register` accepts:

```json
{ "email": "user@example.com", "password": "StrongPass123!" }
```

The response is a pending-confirmation result:

```json
{
  "success": true,
  "message": "Registration successful. Please confirm your email before logging in.",
  "data": { "email": "user@example.com" }
}
```

There is no access token, refresh cookie, or local HeartLog user created by this
request. Show a check-your-email screen. Duplicate-provider registration is handled
with a generic registration response and a resend attempt rather than exposing
whether the account already exists.

### Confirm email

The email link targets
`GET /api/auth/confirm-email?token_hash=TOKEN_HASH&type=email`.
The API verifies the token and responds with a `302` redirect to
`{Frontend:BaseUrl}/email-confirmation?status=success`, `expired`, or `invalid`.
Missing parameters are treated as invalid. The frontend should render the status
and offer login or resend as appropriate. Confirmation does not create a local
user or log the user in; it does not return a JSON session.

### Resend confirmation

`POST /api/auth/resend-confirmation` accepts `{ "email": "user@example.com" }`.
It returns a generic success message:

```json
{
  "success": true,
  "message": "If the account is waiting for confirmation, a new confirmation email has been sent."
}
```

Do not infer account existence or confirmation state from this response. The
endpoint does not create local users, return tokens, or set cookies.

## Login and session refresh

`POST /api/auth/login` accepts the same email/password fields as registration.
Confirmed login and `POST /api/auth/refresh` return this session shape:

```json
{
  "success": true,
  "message": "Login successful",
  "data": {
    "accessToken": "...",
    "expiresAt": "2026-11-11T12:00:00Z",
    "email": "user@example.com"
  }
}
```

The refresh response message is `Session refreshed successfully`. `expiresAt` is
nullable. Neither response exposes the refresh token or `supabaseUserId`.
Login sets `heartlog_refresh_token` as an HttpOnly cookie. JavaScript stores only
access-token session data and application state.

Refresh takes no body and needs no bearer token. The browser sends the refresh
cookie, the API exchanges it with Supabase, and the response renews the cookie and
returns fresh access-token data. Refresh requires an existing local user; it does
not provision one. A missing or rejected refresh cookie returns `401`.

`GET /api/auth/me` requires the bearer token and returns:

```json
{
  "success": true,
  "message": "Current user retrieved successfully",
  "data": {
    "id": "local-heartlog-user-id",
    "username": null,
    "email": "user@example.com"
  }
}
```

This endpoint validates the current session; it does not refresh it.

### Startup and retry behavior

1. Restore access-token state if the application persists it.
2. With a token, load `/api/auth/me` and render the authenticated app on success.
3. On an expired/missing token, the frontend can attempt refresh with credentials,
   then load `/me` using the new token. An HttpOnly cookie cannot be inspected by
   JavaScript, so absence of local token state alone does not prove it is absent.
4. For a protected request returning `401`, refresh and retry the original request
   at most once. Coordinate concurrent refresh attempts to avoid rotation races.
5. If refresh returns `401`, clear auth state and show login. Handle temporary
   provider failures as availability errors rather than entering a retry loop.

## Browser cookies and CORS

Use `credentials: "include"` (or Axios `withCredentials: true`) on login, refresh,
logout, and recovery requests that set or use cookies. Refresh has no JSON body:

```ts
await fetch(`${API_BASE_URL}/api/auth/refresh`, {
  method: "POST",
  credentials: "include"
});
```

Both cookies are HttpOnly and host-scoped. Development uses `SameSite=Lax` and
`Secure=false`; other environments use `SameSite=None` and `Secure=true`.
Cross-origin requests require the exact frontend origin in backend CORS settings
and credential support. Cross-site production cookies require HTTPS and remain
subject to browser cookie restrictions.

| Cookie | Path | Browser expiry set by HeartLog |
| --- | --- | --- |
| `heartlog_refresh_token` | `/api/auth` | 30 days from issuance |
| `heartlog_password_reset_token` | `/api/auth/reset-password` | 15 minutes from issuance |

Cookie expiry does not extend the underlying provider token's validity. Do not
read, store, send, or manually delete refresh tokens in JavaScript.

## Password recovery and password changes

| Endpoint | Authentication | Request and behavior |
| --- | --- | --- |
| `POST /api/auth/forgot-password` | Anonymous | JSON `{ "email": "user@example.com" }`; sends a recovery email when appropriate. |
| `POST /api/auth/forgot-password/me` | Bearer token | No email body; uses the authenticated user's email. |
| `GET /api/auth/reset-password/confirm` | Recovery email token | Query `token_hash=TOKEN_HASH&type=recovery`; verifies the token and sets the short-lived recovery cookie. |
| `POST /api/auth/reset-password` | Recovery cookie | JSON `{ "password": "NewPass123!" }`, with credentials included. |
| `POST /api/auth/change-password` | Bearer token | JSON `{ "currentPassword": "OldPass123!", "newPassword": "NewPass123!" }`. |

Both forgot-password endpoints return the generic message
`If an account exists for this email, a password reset link has been sent.`
The recovery confirmation redirects to
`{Frontend:BaseUrl}/reset-password?status=ready`, `expired`, or `invalid`.
On `ready`, show the password form and submit with credentials; the token stays
in the HttpOnly cookie rather than frontend state or the redirect URL.

Successful recovery reset signs out the recovered user's Supabase sessions and
clears both HeartLog cookies in the current browser. Clear local access-token
state and return to login. Authenticated password change requires the current
password, keeps the current session, and revokes other sessions' refresh tokens.
Already-issued access tokens can remain valid until expiry. Supabase owns password
storage; no local password field is updated. Registration, reset, and change use
the API's password complexity validation.

## Logout and deletion

`POST /api/auth/logout`, with credentials included, clears the refresh cookie.
The frontend must clear its access token and app state. This endpoint does not
currently revoke Supabase sessions server-side; existing bearer tokens can remain
valid until expiry.

For `DELETE /api/auth/me`, password confirmation, cookie cleanup, immediate access
revocation, and cache removal, follow [account deletion](account-deletion.md).

## Error handling

| Condition | HTTP status / code |
| --- | --- |
| Invalid request fields | `400` validation response |
| Invalid login credentials | `401`, `invalidCredentials` |
| Unconfirmed login | `401`, `emailNotConfirmed` |
| Wrong current password | `401`, `invalidCurrentPassword` |
| Missing/invalid access token, missing refresh cookie, missing or pending local user | `401` |
| Authentication provider unavailable | `503`, `authenticationUnavailable` |

Email/recovery confirmation failures use the redirect status described above.
Do not assume a duplicate registration must return an error. Consult OpenAPI for
endpoint-specific error responses.

## Backend and Supabase configuration

Configure backend-only `Supabase__ProjectUrl`, `Supabase__PublishableKey`,
`Supabase__JwtAudience` (default `authenticated`), and `Supabase__SecretKey`.
The secret key is required for account-deletion administration and must never be
sent to the frontend. Set `Frontend__BaseUrl` for confirmation/recovery redirects
and `Cors__AllowedOrigins__0`, `__1`, etc. for allowed browser origins.

Enable email confirmation in the Supabase project. Configure confirmation and
recovery email templates to target the API's actual base URL:

```html
<!-- Signup confirmation -->
<a href="https://YOUR_API_HOST/api/auth/confirm-email?token_hash={{ .TokenHash }}&type=email">Confirm email</a>
<!-- Password recovery -->
<a href="https://YOUR_API_HOST/api/auth/reset-password/confirm?token_hash={{ .TokenHash }}&type=recovery">Reset password</a>
```

With the current local Kestrel configuration, replace `https://YOUR_API_HOST` with
`http://localhost` (port 80), rather than the unused launch-profile port 5048.
