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

namespace HeartLog.Api.Controllers;

[ApiController]
[Route("api/auth")]
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
    public async Task<ActionResult<ApiResponse<AuthRegistrationResponseDto>>> Register(UserRegisterDto userDto)
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
    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<AuthSessionResponseDto>>> Login(UserLoginDto userDto)
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
    public async Task<ActionResult<ApiResponse>> GetSomethingConfidential()
    {
        return Ok(new ApiResponse(Success: true, Message: "Something confidential"));
    }

    [HttpGet("ping")]
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
