# OpenAPI DTO Contract Review

This review records the current DTO contract state for frontend OpenAPI generation. It does not add validation constraints or change DTO shapes.

## Request DTOs

- `UserRegisterDto`
  - `Email` is `[Required]` and `[EmailAddress]`.
  - `Password` is `[Required]` and uses `PasswordComplexity(MinimumLength = 8)`.
- `UserLoginDto`
  - `Email` is `[Required]` and `[EmailAddress]`.
  - `Password` is `[Required]`.
- `ResendConfirmationRequestDto`
  - `Email` is `[Required]` and `[EmailAddress]`.
- `CreateEmotionEntryRequest`
  - `EmotionKeys` is `[Required]` and `[MinLength(1)]`.
  - `PrimaryEmotionKey` is `[Required]`.
  - `Comment` is nullable and currently has no length constraint.
  - `OccurredAt` is nullable and currently has no date-range constraint.
- `ItemDto`
  - `Name` is non-nullable in C# but currently has no `[Required]` or length validation attribute.
  - `Id` is present because `ItemDto` is currently shared by create and response flows.

## Response DTOs

Public response DTOs remain HeartLog-owned API DTOs:

- `AuthRegistrationResponseDto`
- `AuthSessionResponseDto`
- `UserMeResponseDto`
- `EmotionTreeNodeDto`
- `EmotionEntryResponse`
- `EmotionEntriesSummaryResponse`
- `SelectedEmotionResponse`
- `ItemDto`

The API maps provider/auth implementation models into DTOs before returning responses. Supabase and `ExternalAuth*` models are not directly exposed as controller response contracts.

## Response Wrappers

The current API response envelope is:

- `ApiResponse` for success responses without data
- `ApiResponse<T>` for success responses with data
- `ErrorResponse` for error responses

These wrappers are the intended current public contract shape.

## Deferred Behavior Changes

The following are intentionally deferred because they can alter runtime behavior or frontend compatibility:

- adding new validation attributes
- splitting `ItemDto` into create and response DTOs
- adding comment length or date-range constraints
- changing requiredness beyond current annotations and nullable reference type metadata
