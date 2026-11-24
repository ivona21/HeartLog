using System.ComponentModel.DataAnnotations;
using HeartLog.Api.Validation;

namespace HeartLog.Api.DTOs;

public class UserRegisterDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
    
    [Required]
    [PasswordComplexity(MinimumLength = ValidationConstants.PasswordMinimumLength)]
    public string Password { get; set; } = string.Empty;

    [Required]
    public RegistrationAgreements? Agreements { get; set; }
}

public sealed class RegistrationAgreements
{
    public bool TermsAccepted { get; set; }
    public bool PrivacyPolicyAcknowledged { get; set; }
    public bool JournalProcessingConsent { get; set; }
    public string TermsVersion { get; set; } = string.Empty;
    public string PrivacyPolicyVersion { get; set; } = string.Empty;
    public string JournalProcessingVersion { get; set; } = string.Empty;
}
