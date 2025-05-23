using HeartLog.BLL.Interfaces;
using HeartLog.DAL.Models;
using HeartLog.DAL.Repositories;

namespace HeartLog.BLL;

public class UserService: IUserService
{
    private readonly UserRepository _userRepository;
    
    public UserService(UserRepository userRepository)
    {
        _userRepository = userRepository;
    }
    
    public async Task RegisterUserAsync(User user)
    {
        // hash password here
        // send an email - later
        // call repository to save user
        await _userRepository.AddUserAsync(user);
        await _userRepository.SaveChangesAsync();
    }
}