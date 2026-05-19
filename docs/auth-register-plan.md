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
   - Use Supabase JWT `sub` claim as external user id.
   - Verify: endpoint with Supabase token returns 200, missing/invalid token returns 401.

10. Add current user resolution
    - Add a service that reads authenticated `sub`, parses Supabase user id, and loads local `User`.
    - Update `/api/auth/me` to use Supabase id instead of email claim.
    - Verify: `/api/auth/me` returns the local HeartLog user for a Supabase token.

11. Update login flow
    - `POST /api/auth/login` calls Supabase login and returns Supabase session token.
    - Ensure local user exists for the returned Supabase id; decide whether to auto-repair missing local records or fail.
    - Verify: login token works against authorized API endpoints.

12. Cleanup old local JWT auth
    - Remove `JwtTokenGenerator` if no longer used.
    - Remove local password verification.
    - Rename DTOs if needed so response names do not imply local JWTs.
    - Verify: no references to old token generator/password hash path remain.

## Later

- Add email confirmation support.
- Add account linking/recovery behavior if a Supabase user exists but local user creation failed.
- Add transactional compensation: if local DB insert fails after Supabase signup, delete or disable the Supabase user if using an admin-capable API.
- Consider a new Infrastructure project only when there are multiple external integrations or BLL starts accumulating provider-specific code.
