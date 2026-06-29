# Frontend Cookie Auth Agent Prompt

```text
Please update frontend auth integration for HeartLog’s new HttpOnly-cookie refresh-token flow.

Backend auth contract:

1. Login
POST /api/auth/login
Content-Type: application/json
credentials: include

Request:
{
  "email": "user@example.com",
  "password": "password"
}

Success response:
{
  "success": true,
  "message": "Login successful",
  "data": {
    "accessToken": "...",
    "expiresAt": "2026-06-20T10:00:00Z",
    "email": "user@example.com"
  }
}

Important:
- Response no longer includes refreshToken.
- Backend sets refresh token as HttpOnly cookie:
  heartlog_refresh_token
- Frontend must not try to read, store, or send refreshToken manually.

2. Register
POST /api/auth/register
Content-Type: application/json
credentials: include

Same response shape as login.
Also sets heartlog_refresh_token HttpOnly cookie.

3. Refresh
POST /api/auth/refresh
credentials: include
No request body.
No Authorization header required.

Success response:
{
  "success": true,
  "message": "Session refreshed successfully",
  "data": {
    "accessToken": "...",
    "expiresAt": "2026-06-20T11:00:00Z",
    "email": "user@example.com"
  }
}

Important:
- Browser sends heartlog_refresh_token automatically.
- Frontend sends no refreshToken in JSON.
- On success, replace stored accessToken and expiresAt.
- Backend may rotate the refresh cookie.

4. Logout
POST /api/auth/logout
credentials: include

Success response:
{
  "success": true,
  "message": "Logout successful"
}

Frontend behavior:
- Call logout endpoint.
- Clear local accessToken, expiresAt, current user/app auth state.
- Do not manually delete refresh cookie; backend clears it.

5. Current user
GET /api/auth/me
Authorization: Bearer ACCESS_TOKEN

Use this to load local HeartLog user:
{
  "success": true,
  "message": "Current user retrieved successfully",
  "data": {
    "id": "local-heartlog-user-id",
    "username": null,
    "email": "user@example.com"
  }
}

6. Authenticated API calls
Use:
Authorization: Bearer ACCESS_TOKEN

Do not send userId for normal user-owned requests.
Backend resolves the user from token claims.

7. Refresh/retry behavior
When a protected request returns 401:
- If there is local auth state/accessToken, call POST /api/auth/refresh with credentials: include.
- If refresh succeeds, store the new accessToken/expiresAt and retry the original request once.
- If refresh returns 401 or fails, clear auth state and show login.
- Avoid infinite retry loops.

8. App startup behavior
- Restore local accessToken/expiresAt if currently stored.
- Call GET /api/auth/me with Authorization bearer token.
- If /me returns 200, user is authenticated.
- If /me returns 401, call POST /api/auth/refresh with credentials: include.
- If refresh succeeds, retry /me with new accessToken.
- If refresh fails, clear auth state and show login.

9. Storage rules
- Store accessToken and expiresAt only.
- Do not store refreshToken anywhere.
- Do not expect refreshToken in any backend response.
- Do not decode token to determine app user identity; use /api/auth/me.

10. Fetch/client requirements
For requests that set or send cookies, use credentials/include.

Examples:

fetch(`${API_BASE_URL}/api/auth/login`, {
  method: "POST",
  credentials: "include",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({ email, password })
});

fetch(`${API_BASE_URL}/api/auth/refresh`, {
  method: "POST",
  credentials: "include"
});

fetch(`${API_BASE_URL}/api/auth/logout`, {
  method: "POST",
  credentials: "include"
});

If using axios:
- Set withCredentials: true for login/register/refresh/logout.
- Also ensure refresh calls do not send a body or refreshToken.

11. Expected errors
- Missing/invalid/expired accessToken on protected endpoint: 401
- Missing/invalid/expired refresh cookie on /api/auth/refresh: 401
- Bad login credentials: 401
- Valid Supabase token but no linked HeartLog user: 401

Please remove all frontend refreshToken handling from state, storage, DTOs/types, API clients, interceptors, and tests. Replace it with the cookie-based refresh flow above.
```
