using HeartLog.Api.DTOs;
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

    public UsersController(IUserService userService)
    {
        _userService = userService;
        _passwordHasher = new PasswordHasher<User>();
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
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        // Here you would typically validate the user credentials and issue a token
        // For now, we will just return a success message
        return Ok("User logged in successfully");
    }
}