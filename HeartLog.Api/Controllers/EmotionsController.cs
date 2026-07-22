using HeartLog.Api.DTOs;
using HeartLog.Api.Mappers;
using HeartLog.BLL.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace HeartLog.Api.Controllers;

[ApiController]
[Route("api/emotions")]
[Produces("application/json")]
[SwaggerTag("Emotion tree endpoints.")]
public class EmotionsController : ControllerBase
{
    private readonly IEmotionService _emotionService;

    public EmotionsController(IEmotionService emotionService)
    {
        _emotionService = emotionService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<EmotionTreeNodeDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    [SwaggerOperation(OperationId = "Emotions_GetTree")]
    public async Task<ActionResult<ApiResponse<IEnumerable<EmotionTreeNodeDto>>>> GetEmotions([FromQuery] string locale = "en")
    {
        var emotions = await _emotionService.GetEmotionTreeAsync(locale);
        var emotionDtos = emotions.Select(e => e.ToDto()).ToList();

        return Ok(new ApiResponse<IEnumerable<EmotionTreeNodeDto>>(
            Success: true,
            Message: "Emotions retrieved successfully",
            Data: emotionDtos));
    }
}
