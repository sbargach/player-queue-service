using System.ComponentModel.DataAnnotations;

namespace PlayerQueueService.Api.Models.Api;

public class EnqueuePlayerRequest
{
    [Required]
    public Guid PlayerId { get; set; }

    [Range(1, 5000)]
    public int SkillRating { get; set; }

    [Required]
    [StringLength(32)]
    public string Region { get; set; } = string.Empty;

    [Required]
    [StringLength(32)]
    public string GameMode { get; set; } = string.Empty;
}
