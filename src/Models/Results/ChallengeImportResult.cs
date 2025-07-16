namespace PomoChallengeCounter.Models.Results;

public class ChallengeImportResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public Challenge? Challenge { get; set; }
    public int ThreadsProcessed { get; set; }
    public int MessagesProcessed { get; set; }
    public int UsersFound { get; set; }
    public List<string> ThreadsFound { get; set; } = new List<string>();
    public List<string> Warnings { get; set; } = new List<string>();
} 