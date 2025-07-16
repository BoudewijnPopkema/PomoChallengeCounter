namespace PomoChallengeCounter.Services;

/// <summary>
/// Production implementation of ITimeProvider using system DateTime
/// </summary>
public class SystemTimeProvider : ITimeProvider
{
    public DateTime Now => DateTime.Now;
    
    public DateTime UtcNow => DateTime.UtcNow;
    
    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
    
    public DateOnly UtcToday => DateOnly.FromDateTime(DateTime.UtcNow);
} 