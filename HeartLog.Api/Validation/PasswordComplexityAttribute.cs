using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace HeartLog.Api.Validation;

public class PasswordComplexityAttribute: ValidationAttribute
{
    public int MinimumLength { get; set; } = 8;
    
    public override bool IsValid(object value)
    {
        var password = value as string;
        if (string.IsNullOrWhiteSpace(password))
            return false;

        if (password.Length < MinimumLength)
        {
            ErrorMessage = $"Password must be at least {MinimumLength} characters long.";
            return false;
        }

        if (!Regex.IsMatch(password, @"[A-Z]")) // Uppercase
        {
            ErrorMessage = "Password must contain at least one uppercase letter.";
            return false;
        }

        if (!Regex.IsMatch(password, @"[a-z]")) // Lowercase
        {
            ErrorMessage = "Password must contain at least one lowercase letter.";
            return false;
        }

        if (!Regex.IsMatch(password, @"[0-9]")) // Digit
        {
            ErrorMessage = "Password must contain at least one number.";
            return false;
        }

        if (!Regex.IsMatch(password, @"[\W_]")) // Special character
        {
            ErrorMessage = "Password must contain at least one special character.";
            return false;
        }

        return true;
    }
    
}