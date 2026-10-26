using HeartLog.Api.DTOs;
using HeartLog.Api.Mappers;
using HeartLog.BLL.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace HeartLog.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/emotion-entries")]
[Produces("application/json")]
[SwaggerTag("Authenticated emotion entry endpoints.")]
public class EmotionEntriesController : ControllerBase
{
    private readonly IEmotionEntryService _emotionEntryService;
    private readonly ICurrentUserService _currentUserService;

    public EmotionEntriesController(
        IEmotionEntryService emotionEntryService,
        ICurrentUserService currentUserService)
    {
        _emotionEntryService = emotionEntryService;
        _currentUserService = currentUserService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<EmotionEntryResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    [SwaggerOperation(OperationId = "EmotionEntries_GetAll")]
    public async Task<ActionResult<ApiResponse<IEnumerable<EmotionEntryResponse>>>> GetEmotionEntries(CancellationToken cancellationToken)
    {
        var user = await _currentUserService.GetCurrentUserAsync(User, cancellationToken);
        var entries = await _emotionEntryService.GetAllByUserAsync(user.Id, cancellationToken);

        return Ok(new ApiResponse<IEnumerable<EmotionEntryResponse>>(
            Success: true,
            Message: "Emotion entries retrieved successfully",
            Data: entries.ToDto()));
    }

    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ApiResponse<EmotionEntryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    [SwaggerOperation(OperationId = "EmotionEntries_Create")]
    public async Task<ActionResult<ApiResponse<EmotionEntryResponse>>> CreateEmotionEntry([FromBody] CreateEmotionEntryRequest request, CancellationToken cancellationToken)
    {
        var user = await _currentUserService.GetCurrentUserAsync(User, cancellationToken);

        var result = await _emotionEntryService.CreateEmotionEntryAsync(
            user.Id,
            request.EmotionKeys,
            request.PrimaryEmotionKey,
            request.Comment,
            request.OccurredAt,
            cancellationToken);

        return Ok(new ApiResponse<EmotionEntryResponse>(
            Success: true,
            Message: "Emotion entry created successfully",
            Data: result.ToDto()));
    }

    [HttpDelete("{entryId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    [SwaggerOperation(OperationId = "EmotionEntries_Delete")]
    public async Task<IActionResult> DeleteEmotionEntry(Guid entryId, CancellationToken cancellationToken)
    {
        var user = await _currentUserService.GetCurrentUserAsync(User, cancellationToken);
        await _emotionEntryService.DeleteEmotionEntryAsync(user.Id, entryId, cancellationToken);

        return NoContent();
    }

    [HttpGet("summary")]
    [ProducesResponseType(typeof(ApiResponse<EmotionEntriesSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    [SwaggerOperation(OperationId = "EmotionEntries_GetSummary")]
    public async Task<ActionResult<ApiResponse<EmotionEntriesSummaryResponse>>> GetSummary(CancellationToken cancellationToken)
    {
        var user = await _currentUserService.GetCurrentUserAsync(User, cancellationToken);
        var summary = await _emotionEntryService.GetSummaryAsync(user.Id, cancellationToken);

        return Ok(new ApiResponse<EmotionEntriesSummaryResponse>(
            Success: true,
            Message: "Emotion entry summary retrieved successfully",
            Data: summary.ToDto()));
    }
}
