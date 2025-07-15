using System.ComponentModel.DataAnnotations;

namespace PomoChallengeCounter.Models;

public class Server
{
    [Key]
    public ulong Id { get; set; } // Discord Guild ID
    
    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;
    
    [MaxLength(10)]
    public string Language { get; set; } = "en"; // en/nl
    
    [MaxLength(50)]
    public string Timezone { get; set; } = "Europe/Amsterdam";
    
    public ulong? CategoryId { get; set; } // Challenge category ID
    
    public ulong? ConfigRoleId { get; set; } // Role with config permissions
    
    public ulong? PingRoleId { get; set; } // Role to ping for new threads
    
    // Navigation properties
    public ICollection<Challenge> Challenges { get; set; } = new List<Challenge>();
    public ICollection<Emoji> Emojis { get; set; } = new List<Emoji>();
} 