namespace PomoChallengeCounter.Models.Results;

/// <summary>
/// Information about a Discord thread
/// </summary>
public class DiscordThreadInfo
{
    public ulong ThreadId { get; set; }
    public string Name { get; set; } = string.Empty;
    public ulong ChannelId { get; set; }
    public bool IsArchived { get; set; }
    public bool IsLocked { get; set; }
    public DateTime CreatedAt { get; set; }
    public int MessageCount { get; set; }
} 