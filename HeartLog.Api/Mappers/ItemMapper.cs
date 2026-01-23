using HeartLog.Api.DTOs;
using HeartLog.DAL.Models;

namespace HeartLog.Api.Mappers;

public static class ItemMapper
{
    public static ItemDto ToDto(this Item item)
    {
        return new ItemDto
        {
            Name = item.Name
        };
    }

    public static Item ToEntity(this ItemDto dto)
    {
        return new Item
        {
            Name = dto.Name
        };
    }
}
