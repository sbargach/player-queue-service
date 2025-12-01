using System.ComponentModel.DataAnnotations;

namespace PlayerQueueService.Api.Models.Api;

/// <summary>
/// Ensures a string value is not null or composed solely of whitespace.
/// </summary>
public sealed class NotWhitespaceAttribute : ValidationAttribute
{
    public NotWhitespaceAttribute() : base("The {0} field must not be empty or whitespace.")
    {
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null)
        {
            return ValidationResult.Success;
        }

        if (value is string text && !string.IsNullOrWhiteSpace(text))
        {
            return ValidationResult.Success;
        }

        return new ValidationResult(FormatErrorMessage(validationContext.DisplayName));
    }
}
