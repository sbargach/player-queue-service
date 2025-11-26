namespace PlayerQueueService.Api.Models.Api;

public record FieldError(string Field, IEnumerable<string> Errors);
