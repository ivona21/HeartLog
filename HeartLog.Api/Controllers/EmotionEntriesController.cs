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
    [SwaggerOperation(OperationId = "EmotionEntries_GetAll")]
    public async Task<IActionResult> GetEmotionEntries(CancellationToken cancellationToken)
    {
        var user = await _currentUserService.GetCurrentUserAsync(User, cancellationToken);
        var entries = await _emotionEntryService.GetAllByUserAsync(user.Id, cancellationToken);

        return Ok(new ApiResponse<IEnumerable<EmotionEntryResponse>>(
            Success: true,
            Message: "Emotion entries retrieved successfully",
            Data: entries.ToDto()));
    }

    [HttpPost]
    [SwaggerOperation(OperationId = "EmotionEntries_Create")]
    public async Task<IActionResult> CreateEmotionEntry(CreateEmotionEntryRequest request, CancellationToken cancellationToken)
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

    [HttpGet("summary")]
    [SwaggerOperation(OperationId = "EmotionEntries_GetSummary")]
    public async Task<IActionResult> GetSummary(CancellationToken cancellationToken)
    {
        var user = await _currentUserService.GetCurrentUserAsync(User, cancellationToken);
        var summary = await _emotionEntryService.GetSummaryAsync(user.Id, cancellationToken);

        return Ok(new ApiResponse<EmotionEntriesSummaryResponse>(
            Success: true,
            Message: "Emotion entry summary retrieved successfully",
            Data: summary.ToDto()));
    }
}
