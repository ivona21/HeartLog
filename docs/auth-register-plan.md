# Supabase Auth Registration Plan

Goal: use Supabase only for authentication, keep application users in the HeartLog database, and authenticate API requests with Supabase bearer tokens.

## Decisions

- Frontend sends Supabase access JWT on each request.
- Backend validates Supabase JWTs statelessly.
- Local `Users` table stores HeartLog app user data plus Supabase user id.
- Registration takes only email and password for now.
- Registration should return a session for immediate login.
- Email confirmation will be added later.
- Keep the first implementation simple; do not add a separate Infrastructure project yet unless auth/provider code grows.

## Suggested Mini Steps

1. Guard Supabase smoke endpoint
   - Wrap `/test-supabase` in `app.Environment.IsDevelopment()`.
   - Verify: development still exposes endpoint, non-development does not.

2. Move Supabase integration out of DAL
   - Create `HeartLog.BLL/Interfaces/IExternalAuthService.cs`.
   - Move `SupabaseAuthService` and `SupabaseSettings` to BLL for now.
   - Register `IExternalAuthService` to `SupabaseAuthService`.
   - Verify: app builds and `/test-supabase` still works in development.

3. Add external auth contracts
   - Add `ExternalAuthUser` with `Guid ProviderUserId` and `string Email`.
   - Add `ExternalAuthSession` with access token, refresh token if available, expiry if available, and user info.
   - Add `RegisterAsync(email, password)` and `LoginAsync(email, password)` to `IExternalAuthService`.
   - Verify: compile only.

4. Update local user model
   - Add `Guid SupabaseUserId` to `User`.
   - Keep `Email`, `Username`, `CreatedAt`.
   - Temporarily keep `PasswordHash` nullable or leave cleanup for a following migration if needed.
   - Add unique indexes for `SupabaseUserId` and `Email`.
   - Verify: migration generated and reviewed.

5. Add repository lookup by Supabase id
   - Add `GetBySupabaseUserIdAsync(Guid supabaseUserId)`.
   - Keep `GetByEmailAsync` for registration duplicate checks and admin/debug flows.
   - Verify: repository compiles.

6. Implement Supabase registration
   - `SupabaseAuthService.RegisterAsync(email, password)` creates/signs up a Supabase user and returns session/user id.
   - Normalize Supabase exceptions into BLL-level failures where practical.
   - Verify: direct test endpoint or temporary service call can create user in Supabase dev project.

7. Rewrite `UserService.RegisterUserAsync`
   - Check local email conflict first.
   - Call external auth registration.
   - Create local `User` with `SupabaseUserId` and email.
   - Save local user.
   - Return a login/session DTO instead of only success.
   - Verify: `POST /api/auth/register` creates both Supabase auth user and local DB user.

8. Remove local password storage from registration path
   - Stop hashing password.
   - Stop writing `PasswordHash`.
   - Make DB column nullable, remove it, or leave a deprecated nullable field for one migration step.
   - Verify: new registration succeeds without local password hash.

9. Validate Supabase JWTs in API
   - Configure JWT bearer validation for Supabase issuer and signing keys.
   - Current Supabase project uses the new JWT Signing Keys system with an ECC P-256 key (`ES256`), not the legacy shared JWT secret.
   - Derive issuer from `Supabase:ProjectUrl` as `{ProjectUrl}/auth/v1`.
   - Load public signing keys from Supabase JWKS at `{ProjectUrl}/auth/v1/.well-known/jwks.json`.
   - Validate audience as `Supabase:JwtAudience`, defaulting to `authenticated`.
   - Do not use the legacy JWT secret for API bearer-token validation.
   - Preserve the raw Supabase JWT `sub` claim; step 10 uses it as the external user id.
   - Verify: endpoint with Supabase token returns 200, missing/invalid token returns 401.

10. Add current user resolution
    - Add `ICurrentUserService` that reads authenticated `sub`, parses Supabase user id, and loads local `User` by `SupabaseUserId`.
    - Update `/api/auth/me` to use the current-user service instead of email claims.
    - Update user-scoped endpoints to resolve the local user from Supabase `sub` and use local `User.Id` instead of email as identity.
    - Verify: `/api/auth/me` and emotion-entry endpoints return data for the local HeartLog user when called with a Supabase token.

11. Update login flow
    - `POST /api/auth/login` calls Supabase login and returns Supabase session token.
    - Return the same auth session DTO shape as registration.
    - Ensure local user exists for the returned Supabase id.
    - Fail with 401 for now if Supabase login succeeds but the local HeartLog user is missing; account repair/linking is a later concern.
    - Verify: login token works against authorized API endpoints.

12. Cleanup old local JWT auth
    - Remove `JwtTokenGenerator` if no longer used.
    - Remove local password verification.
    - Rename DTOs if needed so response names do not imply local JWTs.
    - Verify: no references to old token generator/password hash path remain.

13. Complete one-off old local user migration
    - One important local user was manually created in Supabase Auth with a known password.
    - The matching local `Users` row was linked by setting `SupabaseUserId`.
    - The local `PasswordHash` was cleared.
    - Other local-only users where `SupabaseUserId` was null were deleted because they were not important.
    - Verify: there are no remaining local-only users and the migrated user can log in through Supabase-backed `/api/auth/login`.

14. Document frontend auth integration
    - Add frontend-facing documentation for the complete auth flow after registration/login cleanup is done.
    - Documentation file: `docs/frontend-auth-flow.md`.
    - Explain that the frontend sends only the Supabase access token for user-owned API calls, not a user id.
    - Document registration flow: call `POST /api/auth/register`, store/use returned Supabase session, then optionally call `GET /api/auth/me` to bootstrap HeartLog user state.
    - Document login flow: call `POST /api/auth/login`, store/use returned Supabase session, then call `GET /api/auth/me` to load the local HeartLog user.
    - Document app startup flow: restore Supabase session, call `GET /api/auth/me` with `Authorization: Bearer {accessToken}`, render authenticated app only if it succeeds.
    - Document user-owned data flow: call endpoints like `GET /api/emotion-entries` with bearer token only; backend resolves local `User.Id` from token `sub`.
    - Document expected auth failures: missing/expired/invalid token returns 401; valid Supabase token with no local HeartLog user also returns 401 until account repair/linking exists.
    - Document token refresh flow: frontend calls HeartLog `POST /api/auth/refresh` with `refreshToken`; frontend does not call Supabase directly.
    - Verify: frontend developer can implement register, login, app bootstrap, logout, and user-owned API calls from the documentation without reading backend code.

## Later

- Add email confirmation support.
- Add account linking/recovery behavior if a Supabase user exists but local user creation failed.
- For future real local-password user migrations, prefer password-reset migration: create/invite Supabase users, link `SupabaseUserId`, and guide users through Supabase password reset instead of preserving local password hashes.
- Add transactional compensation: if local DB insert fails after Supabase signup, delete or disable the Supabase user if using an admin-capable API.
- Consider a new Infrastructure project only when there are multiple external integrations or BLL starts accumulating provider-specific code.
