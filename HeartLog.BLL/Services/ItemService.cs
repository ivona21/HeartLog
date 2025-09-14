using HeartLog.DAL.Models;
using HeartLog.BLL.Interfaces;
using HeartLog.DAL.Repositories;

namespace HeartLog.BLL;

public class ItemService: IItemService
{
    private readonly ItemsRepository _itemsRepository;

    public ItemService(ItemsRepository itemsRepository)
    {
        _itemsRepository = itemsRepository;
    }

    public async Task AddItemAsync(Item item)
    {
        await _itemsRepository.AddItemAsync(item);
    }

    public async Task<IEnumerable<Item>> GetAllItemsAsync()
    {
        return await _itemsRepository.GetAllItemsAsync();
    }
}