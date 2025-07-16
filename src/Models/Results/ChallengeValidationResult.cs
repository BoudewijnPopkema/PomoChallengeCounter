namespace PomoChallengeCounter.Models.Results;

/// <summary>
/// Result of challenge parameter validation
/// </summary>
public class ChallengeValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    
    public static ChallengeValidationResult Valid()
    {
        return new ChallengeValidationResult { IsValid = true };
    }
    
    public static ChallengeValidationResult Invalid(params string[] errors)
    {
        return new ChallengeValidationResult
        {
            IsValid = false,
            Errors = errors.ToList()
        };
    }
    
    public void AddError(string error)
    {
        Errors.Add(error);
        IsValid = false;
    }
} 