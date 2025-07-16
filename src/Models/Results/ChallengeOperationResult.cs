namespace PomoChallengeCounter.Models.Results;

/// <summary>
/// Result of a challenge management operation
/// </summary>
public class ChallengeOperationResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public Challenge? Challenge { get; set; }
    
    public static ChallengeOperationResult Success(Challenge challenge)
    {
        return new ChallengeOperationResult
        {
            IsSuccess = true,
            Challenge = challenge
        };
    }
    
    public static ChallengeOperationResult Failure(string errorMessage)
    {
        return new ChallengeOperationResult
        {
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
    }
} 