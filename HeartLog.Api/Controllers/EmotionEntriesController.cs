using System.IdentityModel.Tokens.Jwt;
using HeartLog.Api.DTOs;
using HeartLog.Api.Mappers;
using HeartLog.BLL.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HeartLog.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/emotion-entries")]
public class EmotionEntriesController : ControllerBase
{
    private readonly IEmotionEntryService _emotionEntryService;

    public EmotionEntriesController(IEmotionEntryService emotionEntryService)
    {
        _emotionEntryService = emotionEntryService;
    }

    [HttpGet]
    public async Task<IActionResult> GetEmotionEntries(CancellationToken cancellationToken)
    {
        var userEmail = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userEmail))
        {
            return Unauthorized(new ErrorResponse { Message = "Authenticated user email was not found." });
        }

        var entries = await _emotionEntryService.GetAllByUserAsync(userEmail, cancellationToken);

        return Ok(new ApiResponse<IEnumerable<EmotionEntryResponse>>(
            Success: true,
            Message: "Emotion entries retrieved successfully",
            Data: entries.ToDto()));
    }

    [HttpPost]
    public async Task<IActionResult> CreateEmotionEntry(CreateEmotionEntryRequest request, CancellationToken cancellationToken)
    {
        var userEmail = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userEmail))
        {
            return Unauthorized(new ErrorResponse { Message = "Authenticated user email was not found." });
        }

        var result = await _emotionEntryService.CreateEmotionEntryAsync(
            userEmail,
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
    public async Task<IActionResult> GetSummary(CancellationToken cancellationToken)
    {
        var userEmail = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userEmail))
        {
            return Unauthorized(new ErrorResponse { Message = "Authenticated user email was not found." });
        }

        var summary = await _emotionEntryService.GetSummaryAsync(userEmail, cancellationToken);

        return Ok(new ApiResponse<EmotionEntriesSummaryResponse>(
            Success: true,
            Message: "Emotion entry summary retrieved successfully",
            Data: summary.ToDto()));
    }
}
