using System.Linq;
using System.ComponentModel.DataAnnotations;
using PlayerQueueService.Api.Models.Events;

namespace PlayerQueueService.Api.Services;

public interface IPlayerEnqueuedEventValidator
{
    ValidationOutcome Validate(PlayerEnqueuedEvent playerEvent);
}

public sealed record ValidationOutcome(bool IsValid, IReadOnlyCollection<string> Errors)
{
    public static ValidationOutcome Success() => new(true, Array.Empty<string>());
    public static ValidationOutcome Failure(IEnumerable<string> errors) => new(false, errors.ToArray());
}

public sealed class PlayerEnqueuedEventValidator : IPlayerEnqueuedEventValidator
{
    public ValidationOutcome Validate(PlayerEnqueuedEvent playerEvent)
    {
        var errors = new List<string>();
        var context = new ValidationContext(playerEvent);
        var results = new List<ValidationResult>();

        if (!Validator.TryValidateObject(playerEvent, context, results, validateAllProperties: true))
        {
            errors.AddRange(results.Select(result => result.ErrorMessage ?? "Validation failed"));
        }

        if (playerEvent.PlayerId == Guid.Empty)
        {
            errors.Add("PlayerId is required.");
        }

        if (string.IsNullOrWhiteSpace(playerEvent.Region))
        {
            errors.Add("Region is required.");
        }

        if (string.IsNullOrWhiteSpace(playerEvent.GameMode))
        {
            errors.Add("GameMode is required.");
        }

        if (string.IsNullOrWhiteSpace(playerEvent.MessageId))
        {
            errors.Add("MessageId is required.");
        }

        if (errors.Count == 0)
        {
            return ValidationOutcome.Success();
        }

        return ValidationOutcome.Failure(errors);
    }
}
