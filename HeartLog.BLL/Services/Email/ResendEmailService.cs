using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HeartLog.BLL.Exceptions;
using HeartLog.BLL.Interfaces;
using HeartLog.BLL.Models.Email;
using HeartLog.BLL.Services.Email.Templates;
using HeartLog.DAL.Models;
using Microsoft.Extensions.Options;

namespace HeartLog.BLL.Services.Email;

public class ResendEmailService(HttpClient httpClient, IOptions<EmailSettings> options) : IEmailService
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.Enabled)
            throw new EmailDeliveryException("Email delivery is disabled.");
        if (message.Template != EmailOutboxMessage.AccountDeletedTemplate
            && message.Template != EmailOutboxMessage.LegacyAccountDeletedTemplate)
            throw new EmailDeliveryException("Unknown email template.", permanent: true);

        const string subject = "Your HeartLog account has been deleted";
        const string legacyText = "Your HeartLog account and emotion entries have been permanently deleted from our active systems.\n\n"
            + "You don’t need to take any further action. If you choose to return, you can register again and start fresh.\n\n"
            + "Thank you for using HeartLog.";
        const string legacyHtml = "<html><body><h1>Your HeartLog account has been deleted</h1>"
            + "<p>Your HeartLog account and emotion entries have been permanently deleted from our active systems.</p>"
            + "<p>You don’t need to take any further action. If you choose to return, you can register again and start fresh.</p>"
            + "<p>Thank you for using HeartLog.</p></body></html>";
        // Preserve the original payload for retries of messages queued before the redesign.
        var legacy = message.Template == EmailOutboxMessage.LegacyAccountDeletedTemplate;
        var text = legacy ? legacyText : AccountDeletedEmailTemplate.Text;
        var html = legacy ? legacyHtml : AccountDeletedEmailTemplate.Html;
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
        {
            Content = JsonContent.Create(new
            {
                from = settings.FromAddress, to = new[] { message.Recipient }, subject, text, html
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        request.Headers.Add("Idempotency-Key", $"account-deleted/{message.Id}");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode) return;
        var retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is null && response.Headers.RetryAfter?.Date is { } date)
            retryAfter = date - DateTimeOffset.UtcNow;
        // Authentication/configuration failures remain retryable so operations can fix them.
        var permanent = response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity;
        throw new EmailDeliveryException($"Email provider returned HTTP {(int)response.StatusCode}.", permanent, retryAfter);
    }
}
