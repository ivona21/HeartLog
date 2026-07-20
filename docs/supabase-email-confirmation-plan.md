# Supabase Mandatory Email Confirmation

This document describes the implemented HeartLog authentication flow for a Supabase project where email confirmation is mandatory.

The design keeps Supabase as the identity provider and HeartLog as the application data owner. Supabase owns credentials, email confirmation, access tokens, and refresh tokens. HeartLog stores local application users in the `Users` table and links them to Supabase users through `Users.SupabaseUserId`.

## Design Summary

Registration does not create an authenticated application session. A user registers, receives a Supabase confirmation email, confirms the email through the backend confirmation endpoint, and then logs in. The local HeartLog user row is created or linked only after the first successful Supabase login.

This separation avoids local application users for accounts that never confirm their email.

Refresh tokens are stored only in the `heartlog_refresh_token` HttpOnly cookie. Access tokens are returned in JSON for authenticated session responses.

## Endpoints

### POST `/api/auth/register`

Starts registration for a new account.

Request:

```json
{
  "email": "user@example.com",
  "password": "StrongPass123!"
}
```

Success response:

```json
{
  "success": true,
  "message": "Registration successful. Please confirm your email before logging in.",
  "data": {
    "email": "user@example.com"
  }
}
```

Application behavior:

- Checks the local `Users` table for an existing email.
- Calls Supabase signup.
- Returns a pending-confirmation response.
- Does not return `accessToken`.
- Does not set `heartlog_refresh_token`.
- Does not create a local HeartLog `Users` row.

Layer flow:

1. `AuthController.Register`
2. `UserMapper.ToEntity`
3. `UserService.RegisterUserAsync`
4. `IUserRepository.GetByEmailAsync`
5. `SupabaseAuthService.RegisterAsync`
6. `client.Auth.SignUp(email, password)`
7. `AuthRegistrationResponseDto`

### GET `/api/auth/confirm-email`

Confirms a Supabase signup email by verifying the Supabase `token_hash`.

Request:

```http
GET /api/auth/confirm-email?token_hash=TOKEN_HASH&type=email
```

Success response:

```json
{
  "success": true,
  "message": "Email confirmed successfully"
}
```

Error behavior:

- Missing `token_hash` returns `400 Bad Request`.
- Missing `type` returns `400 Bad Request`.
- Unsupported `type` is rejected.
- Invalid or expired Supabase token is handled as a controlled authentication provider error.

Application behavior:

- Verifies the token hash with Supabase.
- Confirms the Supabase Auth user email.
- Does not create a local HeartLog `Users` row.
- Does not return `accessToken`.
- Does not set `heartlog_refresh_token`.

Layer flow:

1. `AuthController.ConfirmEmail`
2. `UserService.ConfirmEmailAsync`
3. `SupabaseAuthService.ConfirmEmailAsync`
4. `client.Auth.VerifyTokenHash(tokenHash, Supabase.Gotrue.Constants.EmailOtpType.Email)`

Supabase email templates should generate links using `{{ .TokenHash }}` and `type=email`, pointing at this endpoint.

Example confirmation email link:

```html
<a href="https://YOUR_API_HOST/api/auth/confirm-email?token_hash={{ .TokenHash }}&type=email">
  Confirm email
</a>
```

### POST `/api/auth/resend-confirmation`

Requests another Supabase signup confirmation email.

Request:

```json
{
  "email": "user@example.com"
}
```

Success response:

```json
{
  "success": true,
  "message": "If the account is waiting for confirmation, a new confirmation email has been sent."
}
```

Application behavior:

- Calls Supabase resend confirmation with `type: "signup"`.
- Does not reveal whether the email exists.
- Does not reveal whether the account is already confirmed.
- Does not create a local HeartLog `Users` row.
- Does not return tokens.
- Does not set cookies.

Layer flow:

1. `AuthController.ResendConfirmation`
2. `UserService.ResendConfirmationAsync`
3. `SupabaseAuthService.ResendConfirmationAsync`
4. `POST {Supabase:ProjectUrl}/auth/v1/resend`

Supabase request body:

```json
{
  "type": "signup",
  "email": "user@example.com"
}
```

Supabase request headers:

```http
apikey: SUPABASE_PUBLISHABLE_KEY
Authorization: Bearer SUPABASE_PUBLISHABLE_KEY
```

The public API response remains generic even when Supabase does not send an email for normal user-state reasons. This prevents account enumeration.

### POST `/api/auth/login`

Authenticates a confirmed Supabase user and starts a HeartLog session.

Request:

```json
{
  "email": "user@example.com",
  "password": "StrongPass123!"
}
```

Success response:

```json
{
  "success": true,
  "message": "Login successful",
  "data": {
    "accessToken": "...",
    "expiresAt": "2026-07-20T10:00:00Z",
    "email": "user@example.com"
  }
}
```

Cookie:

```http
Set-Cookie: heartlog_refresh_token=...; HttpOnly; Path=/api/auth; ...
```

Application behavior:

- Calls Supabase login.
- Supabase rejects unconfirmed users.
- Creates or links the local HeartLog user after Supabase authentication succeeds.
- Returns the Supabase access token in JSON.
- Stores the Supabase refresh token in the HttpOnly cookie.
- Does not expose the refresh token in JSON.

Layer flow:

1. `AuthController.Login`
2. `UserService.LoginUserAsync`
3. `SupabaseAuthService.LoginAsync`
4. `client.Auth.SignIn(email, password)`
5. `UserService.EnsureLocalUserExistsOrCreateAsync`
6. `AuthController.SetRefreshTokenCookie`
7. `AuthSessionResponseDto`

Local user linking logic:

1. Look up local user by `SupabaseUserId`.
2. If found, continue.
3. If not found, look up local user by email.
4. If found and `SupabaseUserId` is empty, set it to the Supabase user id.
5. If no local user exists, create one with the Supabase email and user id.
6. If the email belongs to a local user linked to a different Supabase id, reject the login.

### POST `/api/auth/refresh`

Exchanges the HttpOnly refresh-token cookie for a fresh access token.

Request:

```http
POST /api/auth/refresh
Cookie: heartlog_refresh_token=...
```

Success response:

```json
{
  "success": true,
  "message": "Session refreshed successfully",
  "data": {
    "accessToken": "...",
    "expiresAt": "2026-07-20T11:00:00Z",
    "email": "user@example.com"
  }
}
```

Application behavior:

- Reads `heartlog_refresh_token`.
- Exchanges it with Supabase.
- Requires that the Supabase user is already linked to a local HeartLog user.
- Returns a fresh access token.
- Sets or rotates the refresh-token cookie.

Layer flow:

1. `AuthController.Refresh`
2. `UserService.RefreshSessionAsync`
3. `SupabaseAuthService.RefreshAsync`
4. `POST {Supabase:ProjectUrl}/auth/v1/token?grant_type=refresh_token`
5. `UserService.EnsureLocalUserExistsAsync`
6. `AuthController.SetRefreshTokenCookie`
7. `AuthSessionResponseDto`

### GET `/api/auth/me`

Returns the current local HeartLog user for a valid Supabase access token.

Request:

```http
GET /api/auth/me
Authorization: Bearer ACCESS_TOKEN
```

Success response:

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

Application behavior:

- JWT bearer middleware validates the Supabase JWT.
- `CurrentUserService` reads the Supabase `sub` claim.
- The local user is loaded by `Users.SupabaseUserId`.

Layer flow:

1. ASP.NET JWT bearer authentication
2. `AuthController.GetCurrentUser`
3. `CurrentUserService.GetCurrentUserAsync`
4. `IUserRepository.GetBySupabaseUserIdAsync`
5. `UserMeResponseDto`

### POST `/api/auth/logout`

Clears the HeartLog refresh-token cookie.

Request:

```http
POST /api/auth/logout
Cookie: heartlog_refresh_token=...
```

Success response:

```json
{
  "success": true,
  "message": "Logout successful"
}
```

Application behavior:

- Deletes the `heartlog_refresh_token` cookie.
- Does not currently revoke the Supabase refresh token server-side.
- Frontend should clear its local access token and authenticated app state.

Layer flow:

1. `AuthController.Logout`
2. `RefreshTokenCookie.CreateDeleteOptions`
3. `Response.Cookies.Delete`

## Data Ownership

Supabase Auth stores:

- email/password credentials
- email confirmation state
- Supabase user id
- access tokens
- refresh tokens

HeartLog stores:

- local application user id
- user email
- optional username
- `SupabaseUserId`
- user-owned HeartLog data

Registration intentionally creates only a Supabase Auth user. The local HeartLog user is created or linked during confirmed login.

## Cookie Model

Cookie name:

```text
heartlog_refresh_token
```

Cookie path:

```text
/api/auth
```

Cookie properties:

- `HttpOnly = true`
- development: `SameSite=Lax`, `Secure=false`
- non-development: `SameSite=None`, `Secure=true`

The refresh token is never returned in JSON. The frontend stores only access-token session data returned by login or refresh.

## Supabase Configuration

Email confirmation must be enabled in Supabase Auth settings.

The confirmation email should point to the backend confirmation endpoint with Supabase's token hash:

```html
<a href="https://YOUR_API_HOST/api/auth/confirm-email?token_hash={{ .TokenHash }}&type=email">
  Confirm email
</a>
```

For local API testing:

```html
<a href="http://localhost:5048/api/auth/confirm-email?token_hash={{ .TokenHash }}&type=email">
  Confirm email
</a>
```

## Reuse Notes

To reuse this flow in another backend:

1. Treat registration as pending confirmation, not authentication.
2. Verify email confirmation through Supabase `token_hash`.
3. Create local application users only after confirmed login.
4. Store refresh tokens in HttpOnly cookies.
5. Keep resend-confirmation responses generic to avoid account enumeration.
6. Resolve protected-resource ownership from the authenticated Supabase user id, not from frontend-provided user ids.
