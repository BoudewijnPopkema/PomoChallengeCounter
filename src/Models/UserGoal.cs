using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PomoChallengeCounter.Models;

public class UserGoal
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    public ulong UserId { get; set; } // Discord User ID
    
    [Required]
    public int WeekId { get; set; }
    
    [Required]
    public int GoalPoints { get; set; } // Target points for week (computed from goal emojis)
    
    public int ActualPomodoroPoints { get; set; } // Actual pomodoro points achieved
    
    public int ActualBonusPoints { get; set; } // Actual bonus points achieved
    
    public bool IsAchieved { get; set; } // Goal achieved flag (pomodoro + bonus points >= goal)
    
    [MaxLength(255)]
    public string? RewardEmoji { get; set; } // Assigned reward emoji
    
    // Navigation properties
    [ForeignKey(nameof(WeekId))]
    public Week Week { get; set; } = null!;
} 