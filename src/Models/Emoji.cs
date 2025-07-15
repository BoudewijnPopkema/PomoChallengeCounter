using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PomoChallengeCounter.Models;

public class Emoji
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    public ulong ServerId { get; set; }
    
    public int? ChallengeId { get; set; } // NULL for global emojis
    
    [Required]
    [MaxLength(255)]
    public string EmojiCode { get; set; } = string.Empty; // :thumbsup: or <:custom:123> or Unicode
    
    [Required]
    public int PointValue { get; set; } // Points per emoji
    
    [Required]
    public EmojiType EmojiType { get; set; } // Enum: pomodoro, bonus, reward, goal
    
    public bool IsActive { get; set; } = true;
    
    // Navigation properties
    [ForeignKey(nameof(ServerId))]
    public Server Server { get; set; } = null!;
    
    [ForeignKey(nameof(ChallengeId))]
    public Challenge? Challenge { get; set; }
} 