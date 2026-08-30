using HeartLog.Api.Auth;
using HeartLog.Api.DTOs;
using HeartLog.Api.Mappers;
using HeartLog.BLL.Exceptions;
using HeartLog.BLL.Interfaces;
using HeartLog.BLL.Models.Auth;
using HeartLog.DAL.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Swashbuckle.AspNetCore.Annotations;

namespace HeartLog.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
[SwaggerTag("Authentication and current-user endpoints.")]
public class AuthController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public AuthController(
        IUserService userService,
        ICurrentUserService currentUserService,
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        _userService = userService;
        _currentUserService = currentUserService;
        _environment = environment;
        _configuration = configuration;
    }

    [AllowAnonymous]
    [HttpPost("register")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ApiResponse<AuthRegistrationResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    [SwaggerOperation(OperationId = "Auth_Register")]
    public async Task<ActionResult<ApiResponse<AuthRegistrationResponseDto>>> Register([FromBody] UserRegisterDto userDto)
    {
        User user = UserMapper.ToEntity(userDto);
        var registration = await _userService.RegisterUserAsync(user, userDto.Password);

        return Ok(new ApiResponse<AuthRegistrationResponseDto>(
            Success: true,
            Message: "Registration successful. Please confirm your email before logging in.",
            Data: UserMapper.ToDto(registration)));
    }


    [AllowAnonymous]
    [HttpGet("confirm-email")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    [SwaggerOperation(
        OperationId = "Auth_ConfirmEmail",
        Description = "Confirms the email token and redirects to the configured frontend email-confirmation route with status success, expired, or invalid.")]
    public async Task<IActionResult> ConfirmEmail(
        [FromQuery(Name = "token_hash")] string tokenHash,
        [FromQuery] string type)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            return RedirectToEmailConfirmationStatus("invalid");
        }

        if (string.IsNullOrWhiteSpace(type))
        {
            return RedirectToEmailConfirmationStatus("invalid");
        }

        try
        {
            await _userService.ConfirmEmailAsync(tokenHash, type);

            return RedirectToEmailConfirmationStatus("success");
        }
        catch (EmailConfirmationException ex)
        {
            return RedirectToEmailConfirmationStatus(
                ex.Reason == EmailConfirmationFailureReason.Expired
                    ? "expired"
                    : "invalid");
        }
    }

    [AllowAnonymous]
    [HttpPost("resend-confirmation")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    [SwaggerOperation(OperationId = "Auth_ResendConfirmation")]
    public async Task<ActionResult<ApiResponse>> ResendConfirmation([FromBody] ResendConfirmationRequestDto request)
    {
        await _userService.ResendConfirmationAsync(request.Email);

        return Ok(new ApiResponse(
            Success: true,
            Message: "If the account is waiting for confirmation, a new confirmation email has been sent."));
    }

    [AllowAnonymous]
    [HttpPost("forgot-password")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    [SwaggerOperation(OperationId = "Auth_ForgotPassword")]
    public async Task<ActionResult<ApiResponse>> ForgotPassword([FromBody] ForgotPasswordRequestDto request)
    {
        await _userService.SendPasswordResetAsync(request.Email);

        return Ok(new ApiResponse(
            Success: true,
            Message: "If an account exists for this email, a password reset link has been sent."));
    }

    [AllowAnonymous]
    [HttpGet("reset-password/confirm")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    [SwaggerOperation(
        OperationId = "Auth_ConfirmPasswordReset",
        Description = "Confirms the Supabase recovery token, stores a short-lived HTTP-only recovery cookie, and redirects to the configured frontend reset-password route with status ready, expired, or invalid.")]
    public async Task<IActionResult> ConfirmPasswordReset(
        [FromQuery(Name = "token_hash")] string tokenHash,
        [FromQuery] string type)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            return RedirectToPasswordResetStatus("invalid");
        }

        if (string.IsNullOrWhiteSpace(type))
        {
            return RedirectToPasswordResetStatus("invalid");
        }

        try
        {
            var session = await _userService.ConfirmPasswordResetAsync(tokenHash, type);
            SetPasswordResetCookie(session);

            return RedirectToPasswordResetStatus("ready");
        }
        catch (EmailConfirmationException ex)
        {
            return RedirectToPasswordResetStatus(
                ex.Reason == EmailConfirmationFailureReason.Expired
                    ? "expired"
                    : "invalid");
        }
    }

    [AllowAnonymous]
    [HttpPost("reset-password")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    [SwaggerOperation(
        OperationId = "Auth_ResetPassword",
        Description = "Updates the Supabase password using the short-lived HTTP-only recovery cookie. Frontend requests must include credentials.")]
    public async Task<ActionResult<ApiResponse>> ResetPassword([FromBody] ResetPasswordRequestDto request)
    {
        if (!Request.Cookies.TryGetValue(PasswordResetCookie.Name, out var recoveryAccessToken)
            || string.IsNullOrWhiteSpace(recoveryAccessToken))
        {
            throw new UnauthorizedAccessException("Invalid password reset token.");
        }

        await _userService.ResetPasswordAsync(recoveryAccessToken, request.Password);

        Response.Cookies.Delete(
            PasswordResetCookie.Name,
            PasswordResetCookie.CreateDeleteOptions(_environment));

        return Ok(new ApiResponse(
            Success: true,
            Message: "Password reset successful."));
    }
    
    [AllowAnonymous]
    [HttpPost("login")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ApiResponse<AuthSessionResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    [SwaggerOperation(
        OperationId = "Auth_Login",
        Description = "Returns an access token in the response body and sets the HTTP-only heartlog_refresh_token cookie for session refresh.")]
    public async Task<ActionResult<ApiResponse<AuthSessionResponseDto>>> Login([FromBody] UserLoginDto userDto)
    {
        var session = await _userService.LoginUserAsync(userDto.Email, userDto.Password);

        SetRefreshTokenCookie(session);

        return Ok(new ApiResponse<AuthSessionResponseDto>(
            Success: true,
            Message: "Login successful",
            Data: UserMapper.ToDto(session)));
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(ApiResponse<AuthSessionResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    [SwaggerOperation(
        OperationId = "Auth_Refresh",
        Description = "Reads the HTTP-only heartlog_refresh_token cookie, returns a new access token in the response body, and renews the refresh cookie. Frontend requests must include credentials.")]
    public async Task<ActionResult<ApiResponse<AuthSessionResponseDto>>> Refresh()
    {
        if (!Request.Cookies.TryGetValue(RefreshTokenCookie.Name, out var refreshToken)
            || string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new UnauthorizedAccessException("Invalid refresh token.");
        }

        var session = await _userService.RefreshSessionAsync(refreshToken);

        SetRefreshTokenCookie(session);

        return Ok(new ApiResponse<AuthSessionResponseDto>(
            Success: true,
            Message: "Session refreshed successfully",
            Data: UserMapper.ToDto(session)));
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    [SwaggerOperation(
        OperationId = "Auth_Logout",
        Description = "Clears the HTTP-only heartlog_refresh_token cookie. Frontend requests must include credentials.")]
    public ActionResult<ApiResponse> Logout()
    {
        Response.Cookies.Delete(
            RefreshTokenCookie.Name,
            RefreshTokenCookie.CreateDeleteOptions(_environment));

        return Ok(new ApiResponse(
            Success: true,
            Message: "Logout successful"));
    }

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<UserMeResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    [SwaggerOperation(OperationId = "Auth_GetCurrentUser")]
    public async Task<ActionResult<ApiResponse<UserMeResponseDto>>> GetCurrentUser()
    {
        var currentUser = await _currentUserService.GetCurrentUserAsync(User);

        return Ok(new ApiResponse<UserMeResponseDto>(
            Success: true,
            Message: "Current user retrieved successfully",
            Data: UserMapper.ToDto(currentUser)));
    }

    [Authorize]
    [HttpGet("confidential")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    [SwaggerOperation(OperationId = "Auth_GetConfidential")]
    public async Task<ActionResult<ApiResponse>> GetSomethingConfidential()
    {
        return Ok(new ApiResponse(Success: true, Message: "Something confidential"));
    }

    [HttpGet("ping")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    [SwaggerOperation(OperationId = "Auth_Ping")]
    public async Task<ActionResult<ApiResponse>> Ping()
    {
        return Ok(new ApiResponse(Success: true, Message: "Pong"));
    }

    private void SetRefreshTokenCookie(ExternalAuthSession session)
    {
        if (string.IsNullOrWhiteSpace(session.RefreshToken))
        {
            throw new ExternalAuthException("Authentication provider did not return a refresh token.");
        }

        Response.Cookies.Append(
            RefreshTokenCookie.Name,
            session.RefreshToken,
            RefreshTokenCookie.CreateOptions(_environment));
    }

    private void SetPasswordResetCookie(ExternalAuthSession session)
    {
        if (string.IsNullOrWhiteSpace(session.AccessToken))
        {
            throw new ExternalAuthException("Authentication provider did not return a password reset token.");
        }

        Response.Cookies.Append(
            PasswordResetCookie.Name,
            session.AccessToken,
            PasswordResetCookie.CreateOptions(_environment));
    }

    private RedirectResult RedirectToEmailConfirmationStatus(string status)
    {
        var frontendBaseUrl = _configuration["Frontend:BaseUrl"]
                              ?? throw new InvalidOperationException("Frontend base URL is missing.");
        var redirectUrl = $"{frontendBaseUrl.TrimEnd('/')}/email-confirmation";

        var separator = redirectUrl.Contains('?') ? '&' : '?';
        return Redirect($"{redirectUrl}{separator}status={Uri.EscapeDataString(status)}");
    }

    private RedirectResult RedirectToPasswordResetStatus(string status)
    {
        var frontendBaseUrl = _configuration["Frontend:BaseUrl"]
                              ?? throw new InvalidOperationException("Frontend base URL is missing.");
        var redirectUrl = $"{frontendBaseUrl.TrimEnd('/')}/reset-password";

        var separator = redirectUrl.Contains('?') ? '&' : '?';
        return Redirect($"{redirectUrl}{separator}status={Uri.EscapeDataString(status)}");
    }
}
