using HeartLog.Api.DTOs;
using HeartLog.Api.JwtToken;
using Microsoft.AspNetCore.Mvc;
using HeartLog.Api.Mappers;
using HeartLog.BLL.Exceptions;
using HeartLog.DAL.Models;
using HeartLog.BLL.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace HeartLog.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly PasswordHasher<User> _passwordHasher;
    private readonly JwtTokenGenerator _tokenGenerator;

    public UsersController(IUserService userService, JwtTokenGenerator tokenGenerator)
    {
        _userService = userService;
        _passwordHasher = new PasswordHasher<User>();
        _tokenGenerator = tokenGenerator;
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<ApiResponse>> RegisterUser(UserRegisterDto userDto)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState
                .Where(x => x.Value.Errors.Any())
                .ToDictionary(
                    x => x.Key,
                    x => x.Value.Errors.Select(e => e.ErrorMessage).ToArray()
                );

            return BadRequest(new ErrorResponse
            {
                Message = "Validation failed",
                Errors = errors
            });
        }
        
        User user = UserMapper.ToEntity(userDto, _passwordHasher);
        
        try
        {
            await _userService.RegisterUserAsync(user);
        }
        catch (ExistingEmailException ex)
        {
            return BadRequest(new ErrorResponse
            {
                Message =
                    "Unable to register. Please check your input or try logging in if you already have an account.",
                Errors = null
                
            });
        }
        catch (ExistingUsernameException ex)
        {
            return Conflict(new ErrorResponse
            {
                Message = "Username is taken. Please choose another one",
                Errors = null

            });
        }

        // call service
        return Ok(new ApiResponse
            (Success: true, 
                Message: "User registered successfully"
                )
        );
    }
    

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse>> LoginUser(UserLoginDto userDto)
    {
        User existingUser = new User();
        try
        {
            existingUser = await _userService.LoginUserAsync(userDto.Email, userDto.Password);
        }
        catch (Exception ex)
        {
            return BadRequest(new ErrorResponse
            {
                Message = "Unable to login. Please check your credentials.",
                Errors = null
            });
        }

        string token =_tokenGenerator.GenerateToken(existingUser);
        return Ok(new ApiResponse<LoginResponseDto>
            (Success: true, 
                Message: "Login successful", 
                Data:  new LoginResponseDto {
                        Email = existingUser.Email,
            Username = existingUser.Username,
            Token = token}));
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
        return Ok(new ApiResponse(Success:true, Message: "Pong" ));
    }
}