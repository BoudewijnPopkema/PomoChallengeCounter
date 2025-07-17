namespace PomoChallengeCounter.Models.Results;

/// <summary>
/// Result of finding a Discord channel for challenge operations
/// </summary>
public class DiscordChannelResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public ulong ChannelId { get; set; }
    public string? ChannelName { get; set; }
    
    public static DiscordChannelResult Success(ulong channelId, string channelName)
    {
        return new DiscordChannelResult
        {
            IsSuccess = true,
            ChannelId = channelId,
            ChannelName = channelName
        };
    }
    
    public static DiscordChannelResult Failure(string errorMessage)
    {
        return new DiscordChannelResult
        {
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
    }
} 