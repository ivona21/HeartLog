using HeartLog.DAL.Data;
using HeartLog.DAL.Interfaces;
using HeartLog.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace HeartLog.DAL.Repositories;

public class UserRepository : IUserRepository
{
    private readonly ApplicationDbContext _context;
    
    public UserRepository(ApplicationDbContext context)
    {
        _context = context;
    }
    
    // Add a new user to the DbSet but does not save changes yet
    public async Task AddUserAsync(User user)
    {
        await _context.Users.AddAsync(user);
    }

    // Save all changes made in the context to the database
    public async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
    }

    // Optional: Find user by email
    public async Task<User?> GetByEmailAsync(string email)
    {
        return await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
    }

    public async Task<User?> GetBySupabaseUserIdAsync(Guid supabaseUserId)
    {
        return await _context.Users.FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);
    }

    public async Task<User?> GetByUsername(string username)
    {
        return await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
    }
    
}
