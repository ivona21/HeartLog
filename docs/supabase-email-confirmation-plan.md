# Supabase Mandatory Email Confirmation Plan

Goal: adjust HeartLog registration for Supabase projects where email confirmation is mandatory.

## Status

- Step 1 completed: registration now returns a pending-confirmation result instead of an authenticated session.
- Step 2 completed: login now creates or links the local HeartLog user after Supabase authentication succeeds.
- Next step: update frontend/auth docs and run the end-to-end manual confirmation flow.

## Current Problem

Registration currently assumes Supabase returns a fully authenticated session immediately after signup.

That is no longer a valid assumption when Supabase email confirmation is enabled:

- `POST /api/auth/register` should not log the user in immediately.
- Registration should not return an access token.
- Registration should not set the `heartlog_refresh_token` cookie.
- The user must confirm their email first, then log in.

Current code paths affected:

- `HeartLog.BLL/Services/Auth/SupabaseAuthService.cs`
- `HeartLog.BLL/Interfaces/IExternalAuthService.cs`
- `HeartLog.BLL/Interfaces/IUserService.cs`
- `HeartLog.BLL/Services/UserService.cs`
- `HeartLog.Api/Controllers/AuthController.cs`
- `HeartLog.Api/Mappers/UserMapper.cs`
- `HeartLog.Api/DTOs/AuthSessionResponseDto.cs`
- `docs/frontend-auth-flow.md`
- `docs/frontend-cookie-auth-agent-prompt.md`

`HeartLog.Api/Program.cs` should not need functional changes for this work. CORS already allows credentials, and JWT validation remains relevant for login, refresh, and protected endpoints.

## Target Flow

1. User submits registration with email and password.
2. Backend creates the Supabase auth user.
3. Backend returns a registration-pending response.
4. Backend does not create an authenticated HeartLog session.
5. User confirms email through Supabase email link.
6. User logs in with email and password.
7. Backend receives a normal Supabase session.
8. Backend creates or links the local HeartLog user.
9. Backend returns access token and sets the HttpOnly refresh-token cookie.

## Step 1: Change Registration Response Contract

Add a registration-specific auth model in BLL, for example:

```csharp
public class ExternalAuthRegistrationResult
{
    public string Email { get; set; } = string.Empty;
}
```

Change:

```csharp
Task<ExternalAuthSession> RegisterAsync(string email, string password);
```

to:

```csharp
Task<ExternalAuthRegistrationResult> RegisterAsync(string email, string password);
```

Also change `IUserService.RegisterUserAsync` to return the registration result instead of `ExternalAuthSession`.

Expected backend response:

```json
{
  "success": true,
  "message": "Registration successful. Please confirm your email before logging in.",
  "data": {
    "email": "user@example.com"
  }
}
```

Implementation notes:

- `SupabaseAuthService.RegisterAsync` should call `client.Auth.SignUp(email, password)`.
- It should not require `AccessToken`, `RefreshToken`, or `Session`.
- It should return normalized email from Supabase if available; otherwise use the submitted email.
- Keep duplicate-email behavior controlled by the current local check first.

## Step 2: Stop Creating Auth Session On Register

Update `AuthController.Register`:

- Return a registration-specific DTO, not `AuthSessionResponseDto`.
- Remove `SetRefreshTokenCookie(...)` from registration.
- Keep `SetRefreshTokenCookie(...)` only for login and refresh.

Add API DTO, for example:

```csharp
public class AuthRegistrationResponseDto
{
    public string Email { get; set; } = string.Empty;
}
```

## Step 3: Create Or Link Local User On First Confirmed Login

Registration should not create the local `Users` row yet. Create or link the local user only after Supabase login succeeds.

Update `UserService.LoginUserAsync`:

1. Call Supabase login.
2. Try `GetBySupabaseUserIdAsync(session.User.ProviderUserId)`.
3. If found, return the session.
4. If not found, try `GetByEmailAsync(session.User.Email)`.
5. If a local user exists with `SupabaseUserId == null`, set `SupabaseUserId` and save.
6. If no local user exists, create a new `User` with:
   - `Email = session.User.Email`
   - `SupabaseUserId = session.User.ProviderUserId`
   - `CreatedAt = DateTime.UtcNow`
7. Save and return the session.

This replaces the current strict behavior where login fails if Supabase auth succeeds but no local HeartLog user exists.

Repository impact:

- Existing `GetByEmailAsync`, `GetBySupabaseUserIdAsync`, `AddUserAsync`, and `SaveChangesAsync` should be enough.
- No migration should be required because `Users.SupabaseUserId` is already nullable.

## Step 4: Keep Refresh Behavior Strict

`RefreshSessionAsync` should continue requiring an existing linked local user.

Reason: refresh tokens should only work after a confirmed login has created or linked the local HeartLog user.

No expected change:

- `POST /api/auth/refresh`
- `SetRefreshTokenCookie`
- JWT validation in `Program.cs`
- protected endpoint authentication

## Step 5: Frontend Contract Changes

Frontend registration must change:

- Do not expect `accessToken` from `POST /api/auth/register`.
- Do not expect a refresh cookie from register.
- Do not mark the user authenticated after register.
- Show a "check your email" state after successful registration.
- After the user confirms email, send them to login.
- Login remains the only step that starts the authenticated session.

Frontend registration response type:

```ts
type RegisterResponse = {
  email: string;
};
```

Login response type remains unchanged:

```ts
type AuthSessionResponse = {
  accessToken: string;
  expiresAt: string | null;
  email: string;
};
```

Requests that need cookies still require credentials:

```ts
credentials: "include"
```

This still matters for:

- `POST /api/auth/login`
- `POST /api/auth/refresh`
- `POST /api/auth/logout`

It is harmless to keep it on register, but register should not depend on a cookie.

## Step 6: Supabase Configuration To Verify

In Supabase dashboard:

- Email confirmation is enabled.
- Site URL points to the frontend app.
- Redirect URLs include local and deployed frontend confirmation routes.

Examples:

```text
http://localhost:5173/auth/confirmed
https://heart-log-calm.vercel.app/auth/confirmed
```

The frontend route can simply tell the user email confirmation succeeded and offer a login button.

## Step 7: Manual Test Plan

### New User Registration

1. Pick a brand-new email.
2. Call `POST /api/auth/register`.
3. Expected:
   - `200 OK`
   - response contains the registered email
   - response does not contain `accessToken`
   - response does not set `heartlog_refresh_token`
4. Verify Supabase sends confirmation email.

### Login Before Confirmation

1. Try `POST /api/auth/login` before clicking confirmation email.
2. Expected:
   - login fails with `401` or controlled auth error.
   - no refresh cookie is set.
   - no authenticated frontend state is created.

### Login After Confirmation

1. Click the Supabase email confirmation link.
2. Call `POST /api/auth/login`.
3. Expected:
   - `200 OK`
   - response contains `accessToken`, `expiresAt`, and `email`
   - response sets `heartlog_refresh_token`
   - local `Users` row is created or linked.

### Current User

1. Call `GET /api/auth/me` with the login access token.
2. Expected:
   - `200 OK`
   - response returns local HeartLog user id and email.

### Refresh

1. Call `POST /api/auth/refresh` with browser credentials/cookie.
2. Expected:
   - `200 OK`
   - response returns a fresh access token.
   - refresh cookie is kept or rotated.

### Existing User Regression

1. Login with an already confirmed existing user.
2. Expected:
   - login still succeeds.
   - `/api/auth/me` still succeeds.
   - refresh still succeeds.

### Error Regression

Verify:

- Bad password returns `401`.
- Duplicate registration email returns the existing controlled registration error.
- Protected endpoints reject missing or expired tokens.
- Refresh fails without the refresh cookie.

## Step 8: Documentation Updates

Update frontend docs after implementation:

- `docs/frontend-auth-flow.md`
- `docs/frontend-cookie-auth-agent-prompt.md`

Registration section should no longer say register returns the same session shape as login and refresh.

## Implementation Order

1. Add registration result models and DTOs.
2. Change BLL/service/controller registration return types.
3. Remove refresh-cookie creation from register.
4. Add login-time local user creation/linking.
5. Build and fix compile errors.
6. Update docs.
7. Run manual tests against Supabase email confirmation.
