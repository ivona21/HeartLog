using HeartLog.Api.DTOs;
using HeartLog.Api.Mappers;
using HeartLog.BLL.Interfaces;
using HeartLog.DAL.Models;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

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
    [SwaggerOperation(OperationId = "Items_GetAll")]
    public async Task<IActionResult> GetItems()
    {
        IEnumerable<Item> items = await _itemService.GetAllItemsAsync();
        var itemDtos = items.Select(i => new ItemDto
        {
            Name = i.Name,
            Id = i.Id
        });
        
        
        return Ok(new ApiResponse<IEnumerable<ItemDto>>(
            Success: true,
            Message: "Items retrieved successfully",
            Data: itemDtos));
    }

    [HttpPost]
    [SwaggerOperation(OperationId = "Items_Save")]
    public async Task<IActionResult> SaveItem(ItemDto item)
    {
        await _itemService.AddItemAsync(new Item { Name = item.Name });
        return Ok(new ApiResponse<ItemDto>(
            Success: true,
            Message: "Item saved successfully",
            Data: item));
    }
}
