using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PomoChallengeCounter.Models;

public class MessageLog
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    public ulong MessageId { get; set; } // Discord Message ID
    
    [Required]
    public ulong UserId { get; set; } // Discord User ID
    
    [Required]
    public int WeekId { get; set; }
    
    public int PomodoroPoints { get; set; } = 0; // Points from pomodoro emojis
    
    public int BonusPoints { get; set; } = 0; // Points from bonus emojis
    
    public int GoalPoints { get; set; } = 0; // Points from goal emojis
    
    // Navigation properties
    [ForeignKey(nameof(WeekId))]
    public Week Week { get; set; } = null!;
} 