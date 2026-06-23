# Frontend Auth Flow

This document describes the HeartLog auth contract for frontend clients.

## Auth Model

- Supabase is the identity provider.
- The frontend talks to the HeartLog API, not directly to Supabase.
- HeartLog stores local application user data in the `Users` table.
- The backend links local users to Supabase users through `Users.SupabaseUserId`.
- The frontend sends the HeartLog-issued/Supabase-backed access token to protected HeartLog API endpoints.
- The frontend should not send a user id for normal "my data" requests.
- The backend resolves the current local user from the access token `sub` claim.

## Session Shape

`POST /api/auth/register`, `POST /api/auth/login`, and `POST /api/auth/refresh` return the same session shape:

```json
{
  "success": true,
  "message": "Login successful",
  "data": {
    "accessToken": "...",
    "expiresAt": "2026-06-20T10:00:00Z",
    "email": "user@example.com"
  }
}
```

The response intentionally does not expose `supabaseUserId`.

Planned cookie-based refresh-token contract:

- `refreshToken` should not be returned in JSON.
- The backend sets the refresh token in an HttpOnly cookie named `heartlog_refresh_token`.
- JavaScript cannot read the refresh token.
- Frontend stores only `data.accessToken`, `data.expiresAt`, and user/app state.

If the frontend needs the local HeartLog user id, call `GET /api/auth/me`.

## Register

```http
POST /api/auth/register
Content-Type: application/json

{
  "email": "user@example.com",
  "password": "StrongPass123!"
}
```

Frontend behavior:

- Store/use the returned session.
- Use `data.accessToken` as the bearer token for protected API calls.
- Do not expect `data.refreshToken` once the HttpOnly-cookie refresh flow is implemented.
- Include credentials on the request if frontend and API are cross-origin, so the browser accepts the refresh-token cookie.
- Optionally call `GET /api/auth/me` immediately to load the local HeartLog user.

## Login

```http
POST /api/auth/login
Content-Type: application/json

{
  "email": "user@example.com",
  "password": "StrongPass123!"
}
```

Frontend behavior:

- Store/use the returned session.
- Use `data.accessToken` as the bearer token for protected API calls.
- Do not expect `data.refreshToken` once the HttpOnly-cookie refresh flow is implemented.
- Include credentials on the request if frontend and API are cross-origin, so the browser accepts the refresh-token cookie.
- Call `GET /api/auth/me` after login to load the local HeartLog user.
- Bad credentials return `401`.

## Refresh Session

Backend endpoint:

```http
POST /api/auth/refresh
```

Expected response:

```json
{
  "success": true,
  "message": "Session refreshed successfully",
  "data": {
    "accessToken": "...",
    "expiresAt": "2026-06-20T11:00:00Z",
    "email": "user@example.com"
  }
}
```

Important details:

- No bearer token is required for refresh, because the access token may already be expired.
- With the planned HttpOnly-cookie flow, the frontend sends no refresh token in the body.
- The browser sends the `heartlog_refresh_token` cookie automatically when credentials are included.
- The backend exchanges the refresh token from the cookie with Supabase and returns a fresh access-token session.
- Invalid, expired, or missing refresh cookie should return `401`.
- After refresh succeeds, frontend should replace the stored access token and expiry with the returned values.

`GET /api/auth/me` does not refresh tokens. It only validates the current access token and returns the linked local HeartLog user.

## App Startup

Recommended startup flow:

1. Restore the locally stored session.
2. If there is no stored session, show the unauthenticated/login flow.
3. If there is a stored session, call `GET /api/auth/me` with the current access token.
4. If `/me` returns `200`, store the returned HeartLog user and render the authenticated app.
5. If `/me` returns `401`, call `POST /api/auth/refresh` with credentials included.
6. If refresh succeeds, retry `GET /api/auth/me` with the new access token.
7. If refresh fails, clear local auth state and show login.

## Current User

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

Use `/api/auth/me` for local HeartLog user state. Do not decode tokens in the frontend to infer app user identity.

## Authenticated Requests

Send the access token on protected API calls:

```http
Authorization: Bearer ACCESS_TOKEN
```

Do not send `userId` for normal user-owned requests.

Use:

```http
GET /api/emotion-entries
Authorization: Bearer ACCESS_TOKEN
```

Do not use:

```http
GET /api/emotion-entries?userId=...
Authorization: Bearer ACCESS_TOKEN
```

The backend validates the token, reads `sub`, resolves the local `User.Id`, and queries only that user's data.

## Logout

Current expected behavior:

- Frontend calls `POST /api/auth/logout` once the HttpOnly-cookie refresh flow is implemented.
- Backend clears the refresh-token cookie.
- Frontend clears stored access token and local app auth state.

## Expected Errors

- Missing access token on protected endpoint: `401 Unauthorized`.
- Invalid or expired access token: `401 Unauthorized`.
- Missing, invalid, or expired refresh cookie: `401 Unauthorized`.
- Valid Supabase token but no linked local HeartLog user: `401 Unauthorized`.
- Bad login credentials: `401 Unauthorized`.
- Registration email already exists locally: `400 Bad Request`.
- Temporary auth provider issue: controlled authentication error response.

## Important Rules

- Treat `accessToken` as sensitive.
- Refresh tokens should be stored only in an HttpOnly cookie once the cookie-based flow is implemented.
- Do not store or expose `supabaseUserId` in frontend app state unless a future feature explicitly needs it.
- Use `/api/auth/me` for the local HeartLog user id and profile state.
- Do not trust frontend-provided user ids for ownership checks.
- For regular user-owned resources, backend identity always comes from the bearer token.

## Cookie-Based Refresh Requirements

For same-origin frontend/API deployments, normal browser requests are enough.

For cross-origin frontend/API deployments, frontend requests that need to receive or send the refresh cookie must include credentials:

```ts
await fetch("/api/auth/login", {
  method: "POST",
  credentials: "include",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({ email, password })
});

await fetch("/api/auth/refresh", {
  method: "POST",
  credentials: "include"
});

await fetch("/api/auth/logout", {
  method: "POST",
  credentials: "include"
});
```

Backend cookie expectations:

- Cookie name: `heartlog_refresh_token`.
- Cookie is `HttpOnly`.
- Cookie is `Secure` outside local development.
- Cookie uses `SameSite=Lax` for same-site deployments.
- Cookie uses `SameSite=None; Secure` only if frontend and API are on different sites.
- Frontend must not manually read or write this cookie.
