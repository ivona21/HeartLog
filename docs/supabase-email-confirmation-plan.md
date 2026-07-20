# Supabase Mandatory Email Confirmation Plan

Goal: implement the full HeartLog auth flow for Supabase projects where email confirmation is always mandatory.

This document is the working checklist. When continuing later, the next implementation step should be picked from the **Implementation Roadmap** section.

## Current Status

Completed:

- Step 1: `POST /api/auth/register` returns a pending-confirmation response instead of an authenticated session.
- Step 2: `POST /api/auth/login` creates or links the local HeartLog user after Supabase authentication succeeds.
- Step 3: `GET /api/auth/confirm-email` verifies Supabase `token_hash` with `VerifyTokenHash`.
- Register no longer returns `accessToken`.
- Register no longer sets `heartlog_refresh_token`.
- Register no longer creates a local `Users` row.
- Login still returns `accessToken`.
- Login still sets the HttpOnly refresh-token cookie.
- Refresh remains strict and requires an existing linked local user.

Not completed:

- `POST /api/auth/resend-confirmation` does not exist yet.
- Supabase email template/redirect configuration still needs to be aligned with the backend confirmation endpoint.
- Frontend auth docs still need updating.
- End-to-end manual testing still needs to be completed.

## Route Coverage

### POST `/api/auth/register`

Status: implemented for mandatory-confirmation registration.

Purpose:

- Start account creation.
- Ask Supabase to create the Auth user and send confirmation email.
- Do not authenticate the user yet.

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

Current deeper logic:

1. `AuthController.Register` maps `UserRegisterDto` to `User`.
2. `UserService.RegisterUserAsync` checks local DB for existing email.
3. `SupabaseAuthService.RegisterAsync` calls Supabase signup.
4. API returns `AuthRegistrationResponseDto`.

Expected side effects:

- Supabase Auth user is created.
- Supabase confirmation email is sent.
- Local HeartLog `Users` row is not created.
- `heartlog_refresh_token` is not set.
- `accessToken` is not returned.

Future improvement:

- Ensure Supabase confirmation email points to `GET /api/auth/confirm-email` using `token_hash` and `type`.

### GET `/api/auth/confirm-email`

Status: implemented.

Purpose:

- Receive the Supabase email confirmation link callback.
- Verify the `token_hash` with Supabase.
- Confirm the user's email in Supabase.
- Return success or redirect the user to the frontend confirmation-success page.

Expected request:

```http
GET /api/auth/confirm-email?token_hash=TOKEN_HASH&type=email
```

Recommended success behavior:

For API-only testing:

```json
{
  "success": true,
  "message": "Email confirmed successfully"
}
```

For production UX, prefer redirect:

```http
302 Found
Location: https://frontend.example.com/auth/confirmed
```

Recommended error behavior:

- Missing `token_hash`: `400 Bad Request`.
- Unsupported `type`: `400 Bad Request`.
- Invalid or expired token: controlled auth error, preferably `400 Bad Request`.

Implemented deeper-layer changes:

1. Added auth result model:

```csharp
public class ExternalAuthEmailConfirmationResult
{
    public string Email { get; set; } = string.Empty;
}
```

2. Extended `IExternalAuthService`:

```csharp
Task<ExternalAuthEmailConfirmationResult> ConfirmEmailAsync(string tokenHash, string type);
```

3. Implemented `SupabaseAuthService.ConfirmEmailAsync`.

Supabase verification should use the equivalent of:

```ts
supabase.auth.verifyOtp({
  token_hash: tokenHash,
  type: "email"
});
```

Implementation detail:

- The installed Supabase.Gotrue package exposes `VerifyTokenHash`.
- The implementation uses `client.Auth.VerifyTokenHash(tokenHash, Supabase.Gotrue.Constants.EmailOtpType.Email)`.

4. Extended `IUserService`:

```csharp
Task ConfirmEmailAsync(string tokenHash, string type);
```

5. Replaced dummy `AuthController.ConfirmEmail` logic with real service call.

Controller shape:

```csharp
[AllowAnonymous]
[HttpGet("confirm-email")]
public async Task<ActionResult<ApiResponse>> ConfirmEmail(
    [FromQuery(Name = "token_hash")] string tokenHash,
    [FromQuery] string type)
```

Important:

- This endpoint should not create the local HeartLog user.
- Local user creation still happens on first successful login.
- This endpoint should not set `heartlog_refresh_token`.
- This endpoint should not return `accessToken`.

### POST `/api/auth/resend-confirmation`

Status: missing; should be added.

Purpose:

- Let users request a new confirmation email if the first email is missing, expired, or lost.

Request:

```json
{
  "email": "user@example.com"
}
```

Recommended response:

```json
{
  "success": true,
  "message": "If the account is waiting for confirmation, a new confirmation email has been sent."
}
```

Security behavior:

- Do not reveal whether the email exists.
- Do not reveal whether the email is already confirmed.
- Use the same success response for normal user-facing cases.
- Only expose generic controlled errors for provider outages or invalid request shape.

Required deeper-layer changes:

1. Add DTO:

```csharp
public class ResendConfirmationRequestDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}
```

2. Extend `IExternalAuthService`:

```csharp
Task ResendConfirmationAsync(string email);
```

3. Implement `SupabaseAuthService.ResendConfirmationAsync`.

Supabase resend should use the equivalent of:

```ts
supabase.auth.resend({
  type: "signup",
  email
});
```

Implementation detail for .NET:

- First check whether the installed Supabase .NET package exposes resend confirmation.
- If not, call Supabase Auth REST API directly.
- Confirm exact REST endpoint/body from official Supabase docs before coding.

4. Extend `IUserService` if following current service style:

```csharp
Task ResendConfirmationAsync(string email);
```

5. Add controller action:

```csharp
[AllowAnonymous]
[HttpPost("resend-confirmation")]
public async Task<ActionResult<ApiResponse>> ResendConfirmation(ResendConfirmationRequestDto request)
```

Important:

- This endpoint should not create local `Users` rows.
- This endpoint should not set cookies.
- This endpoint should not return tokens.

Future improvement:

- Add rate limiting to avoid abuse.

### POST `/api/auth/login`

Status: implemented for the new flow.

Purpose:

- Authenticate confirmed users.
- Create or link local HeartLog user after Supabase authentication succeeds.
- Start HeartLog app session.

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

```text
Set-Cookie: heartlog_refresh_token=...; HttpOnly; Path=/api/auth; ...
```

Current deeper logic:

1. `AuthController.Login` calls `UserService.LoginUserAsync`.
2. `UserService.LoginUserAsync` calls `SupabaseAuthService.LoginAsync`.
3. Supabase rejects unconfirmed/bad-login users.
4. After Supabase login succeeds, local user is resolved or created:
   - first by `SupabaseUserId`,
   - then by email with empty `SupabaseUserId`,
   - otherwise by creating a new local `User`.
5. Controller sets HttpOnly refresh-token cookie.
6. Controller returns access-token response.

Important:

- Login before email confirmation should fail.
- Login after email confirmation should create/link local `Users` row.

### GET `/api/auth/me`

Status: implemented and still correct.

Purpose:

- Return current local HeartLog user for a valid Supabase access token.

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

Current deeper logic:

1. JWT bearer middleware validates Supabase JWT.
2. `CurrentUserService` reads Supabase `sub`.
3. Local user is loaded by `Users.SupabaseUserId`.

No change needed for confirmation flow.

### POST `/api/auth/logout`

Status: implemented.

Purpose:

- Clear HeartLog frontend session refresh cookie.

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

Current deeper logic:

- Deletes `heartlog_refresh_token`.
- Does not revoke Supabase refresh token server-side.

No change required for confirmation flow.

Future improvement:

- Add Supabase server-side token revocation if needed.

### POST `/api/auth/refresh`

Status: implemented and should stay, even though it was not in the new route list.

Purpose:

- Exchange HttpOnly refresh cookie for a fresh access token.

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

Reason to keep:

- JavaScript cannot read the HttpOnly refresh token.
- The frontend needs a backend endpoint to refresh access tokens.

Current deeper logic:

1. Controller reads `heartlog_refresh_token`.
2. `SupabaseAuthService.RefreshAsync` exchanges it with Supabase.
3. `UserService.RefreshSessionAsync` requires linked local user.
4. Controller rotates/sets refresh cookie.

## Supabase Configuration

Email confirmation must be enabled.

Recommended custom confirmation link:

```text
https://YOUR_API_HOST/api/auth/confirm-email?token_hash={{ .TokenHash }}&type=email
```

For local testing, if Supabase allows the redirect URL:

```text
http://localhost:5048/api/auth/confirm-email?token_hash={{ .TokenHash }}&type=email
```

If using frontend redirect after backend confirmation, add a config value later, for example:

```json
{
  "Frontend": {
    "EmailConfirmedUrl": "http://localhost:5173/auth/confirmed",
    "EmailConfirmationFailedUrl": "http://localhost:5173/auth/confirmation-failed"
  }
}
```

or environment variables:

```env
Frontend__EmailConfirmedUrl=http://localhost:5173/auth/confirmed
Frontend__EmailConfirmationFailedUrl=http://localhost:5173/auth/confirmation-failed
```

Do not add this config until the confirmation endpoint is implemented and redirect behavior is chosen.

## Frontend Contract Changes

Registration:

- Call `POST /api/auth/register`.
- On success, show "check your email".
- Do not store access token.
- Do not mark user authenticated.
- Do not call `/api/auth/me`.

Confirmation:

- If backend returns JSON, frontend can show success after navigating manually.
- If backend redirects, frontend should host:
  - `/auth/confirmed`
  - `/auth/confirmation-failed`

Resend confirmation:

- Add "Resend confirmation email" action on check-email screen.
- Call `POST /api/auth/resend-confirmation`.
- Always show a generic success message.

Login:

- Login remains the authentication entry point.
- Call `POST /api/auth/login` with `credentials: "include"`.
- Store only `accessToken` and `expiresAt`.
- Call `GET /api/auth/me` after login.

Refresh/logout:

- Keep current cookie-based flow.
- Use `credentials: "include"` for login, refresh, and logout.

## Manual Test Plan

### Register

1. Use a brand-new email.
2. Call `POST /api/auth/register`.
3. Expect:
   - `200 OK`
   - response contains registered email
   - no `accessToken`
   - no `heartlog_refresh_token`
   - Supabase Auth user exists
   - local `Users` row does not exist

### Confirm Email

1. Open Supabase confirmation email.
2. Link should call `GET /api/auth/confirm-email?token_hash=...&type=email`.
3. Expect:
   - backend verifies token with Supabase
   - Supabase user becomes confirmed
   - no local `Users` row is created yet
   - no cookie is set
   - no token is returned

### Resend Confirmation

1. Register a new user but do not confirm.
2. Call `POST /api/auth/resend-confirmation`.
3. Expect:
   - generic `200 OK`
   - another confirmation email is sent if Supabase allows it
   - no token/cookie/local user side effects

### Login Before Confirmation

1. Register but do not confirm.
2. Call `POST /api/auth/login`.
3. Expect:
   - login fails
   - no cookie is set
   - no local `Users` row is created

### Login After Confirmation

1. Confirm email.
2. Call `POST /api/auth/login`.
3. Expect:
   - `200 OK`
   - response contains `accessToken`, `expiresAt`, `email`
   - `heartlog_refresh_token` cookie is set
   - local `Users` row is created or linked

### Current User

1. Call `GET /api/auth/me` with bearer access token.
2. Expect:
   - local HeartLog user is returned

### Refresh

1. Call `POST /api/auth/refresh` with browser credentials/cookie.
2. Expect:
   - new access token response
   - refresh cookie kept or rotated

### Logout

1. Call `POST /api/auth/logout`.
2. Expect:
   - refresh cookie deleted
   - frontend clears local access token/app auth state

## Implementation Roadmap

### Completed Step 1: Registration Response Contract

Already done.

Files touched:

- `HeartLog.BLL/Models/Auth/ExternalAuthRegistrationResult.cs`
- `HeartLog.Api/DTOs/AuthRegistrationResponseDto.cs`
- `HeartLog.BLL/Interfaces/IExternalAuthService.cs`
- `HeartLog.BLL/Interfaces/IUserService.cs`
- `HeartLog.BLL/Services/Auth/SupabaseAuthService.cs`
- `HeartLog.BLL/Services/UserService.cs`
- `HeartLog.Api/Mappers/UserMapper.cs`
- `HeartLog.Api/Controllers/AuthController.cs`

### Completed Step 2: Login-Time Local User Creation/Linking

Already done.

Files touched:

- `HeartLog.BLL/Services/UserService.cs`

### Completed Step 3: Implement Real Email Confirmation Endpoint

Done.

Files touched:

- `HeartLog.BLL/Models/Auth/ExternalAuthEmailConfirmationResult.cs`
- `HeartLog.BLL/Interfaces/IExternalAuthService.cs`
- `HeartLog.BLL/Interfaces/IUserService.cs`
- `HeartLog.BLL/Services/Auth/SupabaseAuthService.cs`
- `HeartLog.BLL/Services/UserService.cs`
- `HeartLog.Api/Controllers/AuthController.cs`

Verification:

- BLL build passed.
- API build could not complete in this environment because `dotnet build HeartLog.Api/HeartLog.Api.csproj` repeatedly hung silently and had to be stopped.

### Next Step 4: Add Resend Confirmation Endpoint

Goal:

- Add `POST /api/auth/resend-confirmation`.

Work items:

1. Add `ResendConfirmationRequestDto`.
2. Inspect Supabase .NET SDK support for resend confirmation.
3. If SDK support is insufficient, implement direct REST call in `SupabaseAuthService`.
4. Extend `IExternalAuthService`.
5. Extend `IUserService`.
6. Implement controller endpoint.
7. Use generic response message.
8. Build.
9. Test with unconfirmed user.

### Step 5: Configure Supabase Email Template

Goal:

- Make Supabase confirmation email call the backend confirmation endpoint.

Work items:

1. Update Supabase email template/link to include:

```text
/api/auth/confirm-email?token_hash={{ .TokenHash }}&type=email
```

2. Verify local/deployed API URLs are allowed in Supabase settings.
3. Confirm email from a fresh registration.

### Step 6: Update Frontend

Goal:

- Match the new backend auth contract.

Work items:

1. Register success shows check-email state.
2. Register does not store access token.
3. Register does not mark authenticated.
4. Add resend confirmation action.
5. Add confirmation success/failure routes if backend redirects to frontend.
6. Keep login/refresh/logout cookie behavior.

### Step 7: Update Existing Docs

Goal:

- Update older docs that still describe register as returning a session.

Files:

- `docs/frontend-auth-flow.md`
- `docs/frontend-cookie-auth-agent-prompt.md`

### Step 8: Full E2E Verification

Goal:

- Validate the entire mandatory email confirmation flow.

Run through:

1. Register.
2. Confirm email through backend endpoint.
3. Login.
4. `/me`.
5. Refresh.
6. Logout.
7. Resend confirmation.
8. Existing confirmed user regression.
