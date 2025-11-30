using System.ComponentModel.DataAnnotations;
using PlayerQueueService.Api.Models.Configuration;

namespace PlayerQueueService.Api.Tests;

public class RabbitMQSettingsTests
{
    [Fact]
    public void ValidationFailsWhenRequiredFieldsAreMissing()
    {
        var settings = new RabbitMQSettings
        {
            HostName = string.Empty,
            QueueName = string.Empty,
            ExchangeName = string.Empty,
            RoutingKey = string.Empty
        };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(settings, new ValidationContext(settings), results, validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RabbitMQSettings.HostName)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RabbitMQSettings.QueueName)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RabbitMQSettings.ExchangeName)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RabbitMQSettings.RoutingKey)));
    }

    [Fact]
    public void ValidationPassesWithDefaults()
    {
        var settings = new RabbitMQSettings();
        var results = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(settings, new ValidationContext(settings), results, validateAllProperties: true);

        Assert.True(isValid);
        Assert.Empty(results);
    }

    [Theory]
    [InlineData(0, 2, 5)]
    [InlineData(3, 0, 5)]
    [InlineData(3, 2, 0)]
    public void ValidationFailsWhenRetryOrConfirmationSettingsAreInvalid(
        int maxRetryAttempts,
        int retryDelaySeconds,
        int publishConfirmTimeoutSeconds)
    {
        var settings = new RabbitMQSettings
        {
            MaxRetryAttempts = maxRetryAttempts,
            RetryDelaySeconds = retryDelaySeconds,
            PublishConfirmTimeoutSeconds = publishConfirmTimeoutSeconds
        };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(settings, new ValidationContext(settings), results, validateAllProperties: true);

        Assert.False(isValid);
    }
}
