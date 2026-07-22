using HeartLog.Api.Auth;
using HeartLog.Api.DTOs;
using HeartLog.Api.Mappers;
using HeartLog.BLL.Exceptions;
using HeartLog.BLL.Interfaces;
using HeartLog.BLL.Models.Auth;
using HeartLog.DAL.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    public AuthController(
        IUserService userService,
        ICurrentUserService currentUserService,
        IHostEnvironment environment)
    {
        _userService = userService;
        _currentUserService = currentUserService;
        _environment = environment;
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
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    [SwaggerOperation(OperationId = "Auth_ConfirmEmail")]
    public async Task<ActionResult<ApiResponse>> ConfirmEmail(
        [FromQuery(Name = "token_hash")] string tokenHash,
        [FromQuery] string type)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            return BadRequest(new ErrorResponse
            {
                Message = "Email confirmation token is required."
            });
        }

        if (string.IsNullOrWhiteSpace(type))
        {
            return BadRequest(new ErrorResponse
            {
                Message = "Email confirmation type is required."
            });
        }

        await _userService.ConfirmEmailAsync(tokenHash, type);

        return Ok(new ApiResponse(
            Success: true,
            Message: "Email confirmed successfully"));
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
    [HttpPost("login")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ApiResponse<AuthSessionResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
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
}
