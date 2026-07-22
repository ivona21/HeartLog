# OpenAPI Frontend Contract TODO

Purpose: improve the generated OpenAPI contract so the frontend can reliably discover endpoints, DTOs, auth requirements, response shapes, and validation rules.

Scope rule: OpenAPI/documentation-only changes should not alter runtime behavior. Any item that can change routes, validation, status codes, DTO payloads, auth behavior, or cookie behavior must be reviewed separately before implementation.

## Decisions

- OPENAPI-002: The generated OpenAPI artifact should be committed at `openapi/heartlog.openapi.json`. The frontend should consume that stable file for contract generation. CI or a local verification workflow can later regenerate the file and fail if it is stale.
- OPENAPI-016: Supabase implementation details stay outside the public OpenAPI contract. Controllers expose only HeartLog request/response DTOs and documented auth behavior.
- OPENAPI-037 through OPENAPI-041: Diagnostic, root, health, and development probe endpoints stay callable but are hidden from the public OpenAPI contract.

## Safe Metadata And Export

- [x] OPENAPI-001: Add a repeatable OpenAPI export command or script that writes the generated spec to `openapi/heartlog.openapi.json`.
- [x] OPENAPI-002: Decide whether the generated OpenAPI artifact should be committed, published from CI, or fetched by the frontend from a deployed backend.
- [x] OPENAPI-003: Add OpenAPI generation to CI or a local documented workflow.
- [x] OPENAPI-004: Add `Swashbuckle.AspNetCore.Annotations` if operation IDs and descriptions will be maintained via attributes.
- [x] OPENAPI-005: Enable Swagger annotations in `AddSwaggerGen`.
- [x] OPENAPI-006: Add stable operation IDs for all controller actions.
- [x] OPENAPI-007: Add endpoint tags: `Auth`, `Emotions`, `EmotionEntries`, `Items`, and optionally `Health`.
- [x] OPENAPI-008: Add XML documentation generation in `HeartLog.Api.csproj` if human-readable endpoint descriptions are desired.
- [x] OPENAPI-009: Include XML comments in Swagger configuration if XML documentation is enabled.
- [x] OPENAPI-010: Enable non-nullable reference type support in Swagger configuration with `SupportNonNullableReferenceTypes()`.

## Auth And Security Contract

- [x] OPENAPI-011: Replace global Swagger Bearer security requirement with an operation filter that applies Bearer auth only to actions/controllers with `[Authorize]`.
- [x] OPENAPI-012: Keep `[AllowAnonymous]` explicit on public auth endpoints.
- [x] OPENAPI-013: Document that access tokens are returned in JSON from login/refresh responses.
- [x] OPENAPI-014: Document the HTTP-only refresh token cookie:
  - name: `heartlog_refresh_token`
  - path: `/api/auth`
  - used by: `POST /api/auth/refresh`
  - frontend requirement: requests must include credentials
- [x] OPENAPI-015: Document `POST /api/auth/logout` as clearing the refresh-token cookie.
- [x] OPENAPI-016: Confirm that Supabase internals remain outside the public OpenAPI contract.

## Controller Response Metadata

- [x] OPENAPI-017: Add `[Produces("application/json")]` at controller or global level.
- [x] OPENAPI-018: Add `[Consumes("application/json")]` to POST endpoints that accept JSON bodies.
- [x] OPENAPI-019: Add `[ProducesResponseType]` for success responses on every action.
- [x] OPENAPI-020: Add `[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]` where validation or domain input errors can happen.
- [x] OPENAPI-021: Add `[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]` for authenticated endpoints and auth failure flows.
- [x] OPENAPI-022: Add `[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]` where duplicate/conflict errors can happen.
- [x] OPENAPI-023: Add `[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]` if the frontend should model generic server errors.

## Typed Action Results

- [x] OPENAPI-024: Change `EmotionsController.GetEmotions` from `IActionResult` to `ActionResult<ApiResponse<IEnumerable<EmotionTreeNodeDto>>>`.
- [x] OPENAPI-025: Change `EmotionEntriesController.GetEmotionEntries` from `IActionResult` to `ActionResult<ApiResponse<IEnumerable<EmotionEntryResponse>>>`.
- [x] OPENAPI-026: Change `EmotionEntriesController.CreateEmotionEntry` from `IActionResult` to `ActionResult<ApiResponse<EmotionEntryResponse>>`.
- [x] OPENAPI-027: Change `EmotionEntriesController.GetSummary` from `IActionResult` to `ActionResult<ApiResponse<EmotionEntriesSummaryResponse>>`.
- [x] OPENAPI-028: Change `ItemsController.GetItems` from `IActionResult` to `ActionResult<ApiResponse<IEnumerable<ItemDto>>>`.
- [x] OPENAPI-029: Change `ItemsController.SaveItem` from `IActionResult` to `ActionResult<ApiResponse<ItemDto>>` or to a split response DTO if item contracts are cleaned up.

## Binding Metadata

- [x] OPENAPI-030: Add explicit `[FromBody]` to `AuthController.Register`.
- [x] OPENAPI-031: Add explicit `[FromBody]` to `AuthController.ResendConfirmation`.
- [x] OPENAPI-032: Add explicit `[FromBody]` to `AuthController.Login`.
- [x] OPENAPI-033: Add explicit `[FromBody]` to `EmotionEntriesController.CreateEmotionEntry`.
- [x] OPENAPI-034: Add explicit `[FromBody]` to `ItemsController.SaveItem`.
- [x] OPENAPI-035: Keep `[FromQuery]` on `AuthController.ConfirmEmail` parameters.
- [x] OPENAPI-036: Keep `[FromQuery]` on `EmotionsController.GetEmotions`.

## Endpoint Inventory Review

- [x] OPENAPI-037: Decide whether `GET /api/auth/confidential` should be removed, hidden from OpenAPI, or kept as a real contract endpoint.
- [x] OPENAPI-038: Decide whether `GET /api/auth/ping` should be removed, hidden from OpenAPI, or moved to a health endpoint.
- [x] OPENAPI-039: Decide whether minimal endpoint `GET /ping` should be part of the public contract.
- [x] OPENAPI-040: Decide whether root `GET /` should be hidden from OpenAPI.
- [x] OPENAPI-041: Keep development-only `/test-supabase` out of public OpenAPI.

## Potential Behavior Changes Requiring Separate Review

- [ ] OPENAPI-042: Decide whether create endpoints should return `201 Created` instead of `200 OK`.
- [ ] OPENAPI-043: If using `201 Created`, add meaningful `GET by id` endpoints before using `CreatedAtAction`.
- [ ] OPENAPI-044: Decide whether `ItemsController` route should change from `api/[controller]` to explicit lowercase `api/items`.
- [ ] OPENAPI-045: Decide whether `ItemDto` should be split into `CreateItemRequest` and `ItemResponse`.
- [ ] OPENAPI-046: Decide whether more validation attributes should be added to DTOs.
- [ ] OPENAPI-047: Review any new validation attributes for behavior impact before applying.

## DTO Contract Review

- [ ] OPENAPI-048: Review `UserRegisterDto` required fields and password constraints.
- [ ] OPENAPI-049: Review `UserLoginDto` required fields.
- [ ] OPENAPI-050: Review `ResendConfirmationRequestDto` required fields.
- [ ] OPENAPI-051: Review `CreateEmotionEntryRequest` required fields, list length, comment length, and date behavior.
- [ ] OPENAPI-052: Review `ItemDto` nullability and required fields.
- [ ] OPENAPI-053: Confirm response DTOs do not expose internal or provider-specific models.
- [ ] OPENAPI-054: Confirm `ApiResponse<T>` and `ErrorResponse` are the intended universal wrappers.

## Frontend Consumption

- [ ] OPENAPI-055: Choose frontend contract tooling: `openapi-typescript`, `orval`, `openapi-fetch`, or another generator.
- [ ] OPENAPI-056: Add frontend generation instructions once tooling is chosen.
- [ ] OPENAPI-057: Add an OpenAPI diff/check step to detect breaking changes.
- [ ] OPENAPI-058: Document how the frontend should pass Bearer tokens.
- [ ] OPENAPI-059: Document how the frontend should include cookies for refresh/logout.

## Verification

- [ ] OPENAPI-060: Build backend after metadata changes.
- [ ] OPENAPI-061: Run backend and inspect `/swagger/v1/swagger.json`.
- [ ] OPENAPI-062: Confirm public endpoints do not incorrectly require Bearer auth in OpenAPI.
- [ ] OPENAPI-063: Confirm protected endpoints do require Bearer auth in OpenAPI.
- [ ] OPENAPI-064: Confirm request body schemas are generated for POST endpoints.
- [ ] OPENAPI-065: Confirm expected error response schemas reference `ErrorResponse`.
- [ ] OPENAPI-066: Generate frontend types/client once and check method names, request types, response types, and auth behavior.
