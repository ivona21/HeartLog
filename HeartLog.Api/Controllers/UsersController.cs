using HeartLog.Api.DTOs;
using Microsoft.AspNetCore.Mvc;
using HeartLog.Api.Mappers;
using HeartLog.DAL.Models;
using HeartLog.BLL.Interfaces;

namespace HeartLog.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> RegisterUser(UserRegisterDto userDto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }
        
        User user = UserMapper.ToEntity(userDto);
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
}