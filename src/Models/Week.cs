using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PomoChallengeCounter.Models;

public class Week
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    public int ChallengeId { get; set; }
    
    [Required]
    public int WeekNumber { get; set; } // Week 0 (goals), 1, 2, 3...
    
    public ulong ThreadId { get; set; } // Discord thread ID (required)
    
    public ulong? GoalThreadId { get; set; } // Goal thread ID (week 0 only)
    
    public bool LeaderboardPosted { get; set; } = false;
    
    // Navigation properties
    [ForeignKey(nameof(ChallengeId))]
    public Challenge Challenge { get; set; } = null!;
    
    public ICollection<UserGoal> UserGoals { get; set; } = new List<UserGoal>();
    public ICollection<MessageLog> MessageLogs { get; set; } = new List<MessageLog>();
} 