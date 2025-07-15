using PomoChallengeCounter.Models;

namespace PomoChallengeCounter.Models.Results;

public class MessageProcessingResult
{
    public bool IsSuccess { get; set; }
    public string? Reason { get; set; }
    public MessageLog? MessageLog { get; set; }
    public int DetectedEmojis { get; set; }
} 