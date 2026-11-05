namespace HeartLog.BLL.Services.Email.Templates;

internal static class AccountDeletedEmailTemplate
{
    public const string Text = "Your HeartLog account has been deleted\n\n"
        + "Your HeartLog account and emotion entries have been permanently deleted from our active systems.\n\n"
        + "You don’t need to take any further action. If you ever want to come back, you’re welcome to start fresh with a new account.\n\n"
        + "Thank you for using HeartLog.";

    public const string Html = """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>Your HeartLog account has been deleted</title>
          <style>
            @media only screen and (max-width: 560px) {
              .email-frame { padding: 24px 16px !important; }
              .email-content { padding: 32px 24px !important; }
            }
          </style>
        </head>
        <body style="margin:0;padding:0;background-color:#f8f6fc;">
          <div aria-hidden="true" style="display:none;max-height:0;max-width:0;overflow:hidden;opacity:0;mso-hide:all;">
            You don’t need to take any further action.
          </div>
          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#f8f6fc">
            <tr>
              <td class="email-frame" align="center" style="padding:48px 20px;">
                <!--[if mso]>
                <table role="presentation" width="520" align="center" cellpadding="0" cellspacing="0" border="0">
                  <tr><td>
                <![endif]-->
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"
                       bgcolor="#ffffff" style="width:100%;max-width:520px;border:1px solid #eae4f4;border-radius:16px;border-collapse:separate;">
                  <tr>
                    <td class="email-content" align="left"
                        style="padding:42px 40px;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Arial,sans-serif;color:#625d69;">
                      <p style="margin:0 0 22px;font-size:24px;line-height:30px;font-weight:700;color:#9579d8;">
                        HeartLog
                      </p>
                      <h1 style="margin:0 0 18px;font-size:26px;line-height:34px;font-weight:700;color:#2f2b36;">
                        Your HeartLog account has been&nbsp;deleted
                      </h1>
                      <p style="margin:0 0 16px;font-size:16px;line-height:25px;">
                        Your HeartLog account and emotion entries have been permanently deleted from our active systems.
                      </p>
                      <p style="margin:0 0 16px;font-size:16px;line-height:25px;">
                        You don’t need to take any further action. If you ever want to come back, you’re welcome to start fresh with a new account.
                      </p>
                      <p style="margin:0;font-size:16px;line-height:25px;">
                        Thank you for using HeartLog.
                      </p>
                    </td>
                  </tr>
                </table>
                <!--[if mso]>
                  </td></tr>
                </table>
                <![endif]-->
              </td>
            </tr>
          </table>
        </body>
        </html>
        """;
}
