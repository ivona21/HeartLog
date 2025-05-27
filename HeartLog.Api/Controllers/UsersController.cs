using HeartLog.Api.DTOs;
using HeartLog.Api.JwtToken;
using Microsoft.AspNetCore.Mvc;
using HeartLog.Api.Mappers;
using HeartLog.DAL.Models;
using HeartLog.BLL.Interfaces;
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
        // Here you would typically validate the user credentials and issue a token
        // For now, we will just return a success message
        return Ok(new LoginResponseDto
        {
            Email = existingUser.Email,
            Username = existingUser.Username,
            Token = token
        });
    }
}