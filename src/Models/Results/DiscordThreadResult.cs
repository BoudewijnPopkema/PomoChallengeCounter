namespace PomoChallengeCounter.Models.Results;

/// <summary>
/// Result of a Discord thread creation operation
/// </summary>
public class DiscordThreadResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public ulong ThreadId { get; set; }
    public string? ThreadName { get; set; }
    public bool MessageSent { get; set; }
    
    public static DiscordThreadResult Success(ulong threadId, string threadName, bool messageSent = false)
    {
        return new DiscordThreadResult
        {
            IsSuccess = true,
            ThreadId = threadId,
            ThreadName = threadName,
            MessageSent = messageSent
        };
    }
    
    public static DiscordThreadResult Failure(string errorMessage)
    {
        return new DiscordThreadResult
        {
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
    }
} 