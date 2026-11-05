using HeartLog.Api.DTOs;
using HeartLog.Api.Errors;
using HeartLog.BLL.Exceptions;
using HeartLog.BLL.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HeartLog.Api.Controllers;

// Diagnostic available for execution only in Development.
[ApiController]
[AllowAnonymous]
[Route("api/dev/email")]
[Produces("application/json")]
public class EmailDiagnosticsController(IEmailOutboxService emailOutbox, IHostEnvironment environment) : ControllerBase
{
    [HttpPost("account-deletion")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ApiResponse<QueuedEmailResponseDto>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ApiResponse<QueuedEmailResponseDto>>> SendAccountDeletionEmail(
        [FromBody] SendTestEmailRequestDto request, CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment()) return NotFound();

        try
        {
            var messageId = await emailOutbox.QueueAccountDeletionConfirmationAsync(request.Email.Trim(), cancellationToken);
            return Accepted(new ApiResponse<QueuedEmailResponseDto>(
                Success: true,
                Message: "Test email queued. No account was deleted. Delivery is processed by the email worker.",
                Data: new QueuedEmailResponseDto(messageId)));
        }
        catch (EmailDeliveryException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorFactory.Create(
                ApiErrorCode.UnexpectedError, HttpContext.TraceIdentifier,
                message: "Email delivery is disabled. Enable and configure Email settings before testing."));
        }
    }
}
