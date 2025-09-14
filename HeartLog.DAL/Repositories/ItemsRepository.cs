using HeartLog.DAL.Data;
using HeartLog.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace HeartLog.DAL.Repositories;

public class ItemsRepository
{
    private readonly ApplicationDbContext _context;

    public ItemsRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Item>> GetAllItemsAsync()
    {
        return await _context.Items.ToListAsync();
    }

    public async Task AddItemAsync(Item item)
    {
        await _context.Items.AddAsync(item);
        await _context.SaveChangesAsync();
    }
}