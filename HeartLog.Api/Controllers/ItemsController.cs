using HeartLog.Api.DTOs;
using HeartLog.BLL.Interfaces;
using HeartLog.DAL.Models;
using Microsoft.AspNetCore.Mvc;

namespace HeartLog.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ItemsController: ControllerBase
{
    private readonly IItemService _itemService;

    public ItemsController(IItemService itemService)
    {
        _itemService = itemService;   
    }
    
    [HttpGet]
    public async Task<IActionResult> GetItems()
    {
        return Ok(await _itemService.GetAllItemsAsync());
    }

    [HttpPost]
    public async Task<IActionResult> SaveItem(ItemDto item)
    {
        await _itemService.AddItemAsync(new Item { Name = item.Name });
        return Ok();
    }
}