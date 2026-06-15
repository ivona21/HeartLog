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

    public AuthController(IUserService userService)
    {
        _userService = userService;
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
    public async Task<ActionResult<ApiResponse<LoginResponseDto>>> Login(UserLoginDto userDto)
    {
        string token = await _userService.LoginUserAsync(userDto.Email, userDto.Password);
        
        // Note: Ideally UserService should return more user info if needed, 
        // but for now we'll just return the token and basic info from input
        return Ok(new ApiResponse<LoginResponseDto>
        (Success: true,
            Message: "Login successful",
            Data: new LoginResponseDto
            {
                Email = userDto.Email,
                Token = token
                // Username would ideally come from the service result
            }));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<ApiResponse<UserMeResponseDto>>> GetCurrentUser()
    {
        var userEmail = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userEmail))
        {
            return Unauthorized(new ErrorResponse { Message = "Authenticated user email was not found." });
        }

        var currentUser = await _userService.GetCurrentUserAsync(userEmail);

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
