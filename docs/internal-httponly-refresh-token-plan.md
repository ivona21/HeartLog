# Internal Plan: HttpOnly Refresh Token Cookie

Temporary implementation checklist. Delete this file after the implementation is completed and the public frontend contract is accurate.

## Goal

Move refresh-token storage out of frontend-readable JavaScript state and into a backend-set HttpOnly cookie.

The frontend should continue receiving the short-lived access token in JSON, but should no longer receive or send the refresh token in JSON.

## Backend Implementation Steps

1. Add refresh cookie constants/options - done
   - Cookie name: `heartlog_refresh_token`.
   - `HttpOnly = true`.
   - `Secure = true` outside local development.
   - `SameSite = Lax` for same-site frontend/backend deployments.
   - Use `SameSite = None` plus `Secure = true` if frontend and API are on different sites.
   - Path should be scoped to `/api/auth` unless a broader path is needed.
   - Expiration should align with the refresh token lifetime policy. If exact Supabase refresh expiry is unavailable, use a conservative configured lifetime.

2. Update auth session API response shape - done
   - Remove `refreshToken` from frontend-facing JSON responses.
   - Keep `accessToken`, `expiresAt`, and `email`.
   - Do not expose `supabaseUserId`.

3. Update register/login endpoints - done
   - After Supabase returns a session, set the refresh-token cookie if a refresh token is present.
   - Return only the access-token session payload in JSON.
   - If Supabase does not return a refresh token for a successful login/register, treat it as an auth-provider error.

4. Update refresh endpoint - done
   - Change `POST /api/auth/refresh` to read the refresh token from the HttpOnly cookie.
   - Do not require a request body.
   - Exchange the cookie refresh token with Supabase.
   - If Supabase returns a new refresh token, overwrite the cookie with the new value.
   - Return only the fresh access-token session payload in JSON.

5. Add logout endpoint - done
   - Add `POST /api/auth/logout`.
   - Clear the refresh-token cookie.
   - Return a simple success response.
   - This is frontend logout from HeartLog's perspective. Supabase server-side session revocation can be added later if needed.

6. Update CORS if needed
   - If browser frontend and API are cross-origin, configure CORS to allow the frontend origin and credentials.
   - Do not use wildcard origins with credentials.

7. Update error handling
   - Missing refresh cookie should return `401`.
   - Invalid or expired refresh cookie should return `401`.
   - Temporary Supabase/provider failures should remain controlled auth-provider errors.

8. Verify
   - Login response includes `Set-Cookie` for `heartlog_refresh_token`.
   - Login JSON does not include `refreshToken`.
   - Refresh succeeds with cookie and no body.
   - Refresh response rotates/updates cookie when Supabase returns a new refresh token.
   - Refresh without cookie returns `401`.
   - Logout clears the cookie.
   - Protected endpoints still require `Authorization: Bearer ACCESS_TOKEN`.

## Frontend Coordination

- Frontend must use credentialed requests for auth endpoints that set or send cookies.
- Frontend stores the access token only.
- Frontend does not read, persist, or send refresh tokens manually.
- On access-token expiry or protected API `401`, frontend calls `POST /api/auth/refresh` with credentials included, then retries once with the new access token.
- If refresh fails with `401`, frontend clears auth state and shows login.
