using HeartLog.Api.DTOs;
using HeartLog.Api.Mappers;
using HeartLog.BLL.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace HeartLog.Api.Controllers;

[ApiController]
[Route("api/emotions")]
[SwaggerTag("Emotion tree endpoints.")]
public class EmotionsController : ControllerBase
{
    private readonly IEmotionService _emotionService;

    public EmotionsController(IEmotionService emotionService)
    {
        _emotionService = emotionService;
    }

    [HttpGet]
    [SwaggerOperation(OperationId = "Emotions_GetTree")]
    public async Task<IActionResult> GetEmotions([FromQuery] string locale = "en")
    {
        var emotions = await _emotionService.GetEmotionTreeAsync(locale);
        var emotionDtos = emotions.Select(e => e.ToDto()).ToList();

        return Ok(new ApiResponse<IEnumerable<EmotionTreeNodeDto>>(
            Success: true,
            Message: "Emotions retrieved successfully",
            Data: emotionDtos));
    }
}
