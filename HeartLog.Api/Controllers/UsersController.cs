using HeartLog.Api.DTOs;
using HeartLog.Api.JwtToken;
using Microsoft.AspNetCore.Mvc;
using HeartLog.Api.Mappers;
using HeartLog.DAL.Models;
using HeartLog.BLL.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace HeartLog.Api.Controllers;

[ApiController]
[Authorize]
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
    public async Task<IActionResult> RegisterUser(UserRegisterDto userDto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }
        
        User user = UserMapper.ToEntity(userDto, _passwordHasher);
        try
        {
            await _userService.RegisterUserAsync(user);
        } catch (Exception ex)
        {
            return BadRequest("Unable to register. Please check your input or try logging in if you already have an account.");
        }

        // call service
        return Ok("User registered successfully");
    }
    
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> LoginUser(UserLoginDto userDto)
    {
        User existingUser = new User();
        try
        {
            existingUser = await _userService.LoginUserAsync(userDto.Email, userDto.Password);
        }
        catch (Exception ex)
        {
            return BadRequest("Unable to login. Please check your credentials.");
        }

        string token =_tokenGenerator.GenerateToken(existingUser);
        return Ok(new LoginResponseDto
        {
            Email = existingUser.Email,
            Username = existingUser.Username,
            Token = token
        });
    }
    
    [Authorize]
    [HttpGet("confidential")]
    public async Task<IActionResult> GetSomethingConfidential()
    {
        return Ok(new { Message = "Something confidential :)" });
    }
}