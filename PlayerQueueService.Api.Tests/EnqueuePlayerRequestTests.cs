using System.ComponentModel.DataAnnotations;
using PlayerQueueService.Api.Models.Api;

namespace PlayerQueueService.Api.Tests;

public class EnqueuePlayerRequestTests
{
    [Fact]
    public void ValidationFailsWhenRegionOrGameModeAreWhitespace()
    {
        var request = new EnqueuePlayerRequest
        {
            PlayerId = Guid.NewGuid(),
            SkillRating = 1500,
            Region = "   ",
            GameMode = "\t"
        };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(EnqueuePlayerRequest.Region)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(EnqueuePlayerRequest.GameMode)));
    }

    [Fact]
    public void ValidationPassesWhenRegionAndGameModeContainCharacters()
    {
        var request = new EnqueuePlayerRequest
        {
            PlayerId = Guid.NewGuid(),
            SkillRating = 1500,
            Region = " na ",
            GameMode = " duos "
        };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);

        Assert.True(isValid);
        Assert.Empty(results);
    }
}
