using System.ComponentModel.DataAnnotations;
using PlayerQueueService.Api.Models.Configuration;

namespace PlayerQueueService.Api.Tests;

public class RabbitMqOptionsTests
{
    [Fact]
    public void ValidationFailsWhenRequiredFieldsAreMissing()
    {
        var options = new RabbitMqOptions
        {
            HostName = string.Empty,
            QueueName = string.Empty,
            ExchangeName = string.Empty,
            RoutingKey = string.Empty
        };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RabbitMqOptions.HostName)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RabbitMqOptions.QueueName)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RabbitMqOptions.ExchangeName)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RabbitMqOptions.RoutingKey)));
    }

    [Fact]
    public void ValidationPassesWithDefaults()
    {
        var options = new RabbitMqOptions();
        var results = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        Assert.True(isValid);
        Assert.Empty(results);
    }
}
