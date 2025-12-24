using System.ComponentModel.DataAnnotations;
using NUnit.Framework;
using PlayerQueueService.Api.Models.Configuration;
using Shouldly;

namespace PlayerQueueService.Api.Tests;

public class RabbitMQSettingsTests
{
    [Test]
    public void ValidationFailsWhenRequiredFieldsAreMissing()
    {
        var settings = new RabbitMQSettings
        {
            HostName = string.Empty,
            QueueName = string.Empty,
            ExchangeName = string.Empty,
            RoutingKey = string.Empty,
            MatchResultsExchangeName = string.Empty,
            MatchResultsQueueName = string.Empty,
            MatchResultsRoutingKey = string.Empty
        };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(settings, new ValidationContext(settings), results, validateAllProperties: true);

        isValid.ShouldBeFalse();
        results.ShouldContain(r => r.MemberNames.Contains(nameof(RabbitMQSettings.HostName)));
        results.ShouldContain(r => r.MemberNames.Contains(nameof(RabbitMQSettings.QueueName)));
        results.ShouldContain(r => r.MemberNames.Contains(nameof(RabbitMQSettings.ExchangeName)));
        results.ShouldContain(r => r.MemberNames.Contains(nameof(RabbitMQSettings.RoutingKey)));
        results.ShouldContain(r => r.MemberNames.Contains(nameof(RabbitMQSettings.MatchResultsExchangeName)));
        results.ShouldContain(r => r.MemberNames.Contains(nameof(RabbitMQSettings.MatchResultsQueueName)));
        results.ShouldContain(r => r.MemberNames.Contains(nameof(RabbitMQSettings.MatchResultsRoutingKey)));
    }

    [Test]
    public void ValidationPassesWithDefaults()
    {
        var settings = new RabbitMQSettings();
        var results = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(settings, new ValidationContext(settings), results, validateAllProperties: true);

        isValid.ShouldBeTrue();
        results.ShouldBeEmpty();
    }

    [TestCase(0, 2, 5)]
    [TestCase(3, 0, 5)]
    [TestCase(3, 2, 0)]
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

        isValid.ShouldBeFalse();
    }
}
