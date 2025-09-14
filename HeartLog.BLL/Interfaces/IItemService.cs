using HeartLog.DAL.Models;

namespace HeartLog.BLL.Interfaces;

public interface IItemService
{
    Task AddItemAsync(Item item);
    Task<IEnumerable<Item>> GetAllItemsAsync();
}