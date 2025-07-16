using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PomoChallengeCounter.Models;

public class Challenge
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    public ulong ServerId { get; set; }
    
    [Required]
    public int SemesterNumber { get; set; } // S1, S2, S3, S4, S5 (1-4: regular semesters, 5: summer)
    
    [Required]
    [MaxLength(255)]
    public string Theme { get; set; } = string.Empty;
    
    [Required]
    [Column(TypeName = "date")]
    public DateOnly StartDate { get; set; } // Monday start
    
    [Required]
    [Column(TypeName = "date")]
    public DateOnly EndDate { get; set; } // Sunday end
    
    [Required]
    public int WeekCount { get; set; }
    
    public ulong? ChannelId { get; set; } // Discord channel ID
    
    public bool IsCurrent { get; set; } = false; // Whether this is the current active challenge
    
    public bool IsStarted { get; set; } = false;
    
    public bool IsActive { get; set; } = true; // Whether challenge is active (processing messages)
    
    // Navigation properties
    [ForeignKey(nameof(ServerId))]
    public Server Server { get; set; } = null!;
    
    public ICollection<Week> Weeks { get; set; } = new List<Week>();
    public ICollection<Emoji> Emojis { get; set; } = new List<Emoji>();
} 