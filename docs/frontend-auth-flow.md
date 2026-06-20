# Frontend Auth Flow

This document describes how frontend clients should use HeartLog auth after the Supabase migration.

## Auth Model

- Supabase is the identity provider.
- HeartLog keeps local application user data in the `Users` table.
- The backend links local users to Supabase users through `Users.SupabaseUserId`.
- The frontend sends the Supabase access token to HeartLog API endpoints.
- The frontend should not send a user id for normal "my data" requests.
- The backend resolves the current local user from the token `sub` claim.

## Session Response

`POST /api/auth/register` and `POST /api/auth/login` return the same session shape:

```json
{
  "success": true,
  "message": "Login successful",
  "data": {
    "accessToken": "...",
    "refreshToken": "...",
    "expiresAt": "2026-06-20T10:00:00Z",
    "email": "user@example.com"
  }
}
```

The response intentionally does not expose `supabaseUserId`.

If the frontend needs the local HeartLog user id, call `GET /api/auth/me`.

## Register

Request:

```http
POST /api/auth/register
Content-Type: application/json

{
  "email": "user@example.com",
  "password": "StrongPass123!"
}
```

Frontend behavior:

- Store/use the returned Supabase session.
- Use `data.accessToken` as the bearer token for API calls.
- Optionally call `GET /api/auth/me` immediately to load the local HeartLog user.

## Login

Request:

```http
POST /api/auth/login
Content-Type: application/json

{
  "email": "user@example.com",
  "password": "StrongPass123!"
}
```

Frontend behavior:

- Store/use the returned Supabase session.
- Call `GET /api/auth/me` after login to load the local HeartLog user.
- Bad credentials return `401`.

## App Startup

Recommended startup flow:

1. Restore the Supabase session on the frontend.
2. If there is no session, show the unauthenticated/login flow.
3. If there is a session, call `GET /api/auth/me` with the access token.
4. If `/me` returns `200`, store the returned HeartLog user and render the authenticated app.
5. If `/me` returns `401`, clear the local session state and show login.

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

## Authenticated Requests

Send the access token on protected API calls:

```http
Authorization: Bearer ACCESS_TOKEN
```

Do not send `userId` for normal user-owned requests. For example, use:

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

## Token Refresh

The frontend is responsible for refreshing the Supabase session/access token before API calls.

The HeartLog API validates bearer tokens but does not refresh them.

If an API request returns `401` because the token is expired:

1. Refresh the Supabase session on the frontend.
2. Retry the API request with the new access token.
3. If refresh fails, sign the user out locally and show login.

## Logout

Current expected behavior:

- Frontend signs out through Supabase client/session handling.
- Frontend clears local app auth state.
- No HeartLog backend logout endpoint is currently required.

## Expected Errors

- Missing token: `401 Unauthorized`.
- Invalid or expired token: `401 Unauthorized`.
- Valid Supabase token but no linked local HeartLog user: `401 Unauthorized`.
- Bad login credentials: `401 Unauthorized`.
- Registration email already exists locally: `400 Bad Request`.
- Temporary auth provider issue: controlled authentication error response.

## Important Rules

- Treat the access token as sensitive.
- Do not store or expose `supabaseUserId` in frontend app state unless a future feature explicitly needs it.
- Use `/api/auth/me` for the local HeartLog user id and profile state.
- Do not trust frontend-provided user ids for ownership checks.
- For regular user-owned resources, backend identity always comes from the bearer token.
