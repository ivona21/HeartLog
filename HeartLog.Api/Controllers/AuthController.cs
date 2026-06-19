using HeartLog.Api.DTOs;
using HeartLog.Api.Mappers;
using HeartLog.BLL.Interfaces;
using HeartLog.DAL.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HeartLog.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ICurrentUserService _currentUserService;

    public AuthController(
        IUserService userService,
        ICurrentUserService currentUserService)
    {
        _userService = userService;
        _currentUserService = currentUserService;
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<ApiResponse<AuthSessionResponseDto>>> Register(UserRegisterDto userDto)
    {
        User user = UserMapper.ToEntity(userDto);
        var session = await _userService.RegisterUserAsync(user, userDto.Password);

        return Ok(new ApiResponse<AuthSessionResponseDto>(
            Success: true,
            Message: "User registered successfully",
            Data: UserMapper.ToDto(session)));
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<AuthSessionResponseDto>>> Login(UserLoginDto userDto)
    {
        var session = await _userService.LoginUserAsync(userDto.Email, userDto.Password);

        return Ok(new ApiResponse<AuthSessionResponseDto>(
            Success: true,
            Message: "Login successful",
            Data: UserMapper.ToDto(session)));
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
}
