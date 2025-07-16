namespace PomoChallengeCounter.Services;

/// <summary>
/// Abstraction for time operations to enable testable time-dependent code
/// </summary>
public interface ITimeProvider
{
    /// <summary>
    /// Gets the current local date and time
    /// </summary>
    DateTime Now { get; }
    
    /// <summary>
    /// Gets the current UTC date and time
    /// </summary>
    DateTime UtcNow { get; }
    
    /// <summary>
    /// Gets the current local date
    /// </summary>
    DateOnly Today { get; }
    
    /// <summary>
    /// Gets the current UTC date
    /// </summary>
    DateOnly UtcToday { get; }
} 