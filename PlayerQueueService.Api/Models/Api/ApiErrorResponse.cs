namespace PlayerQueueService.Api.Models.Api;

public record ApiErrorResponse(string Message, IEnumerable<FieldError> Errors)
{
    public static ApiErrorResponse ValidationFailed(IEnumerable<FieldError> errors) =>
        new("Validation failed", errors);
}
