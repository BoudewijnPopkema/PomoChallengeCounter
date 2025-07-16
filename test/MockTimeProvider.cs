using PomoChallengeCounter.Services;

namespace PomoChallengeCounter.Tests;

/// <summary>
/// Mock time provider for testing that allows time manipulation
/// </summary>
public class MockTimeProvider : ITimeProvider
{
    private DateTime _fixedTime = new DateTime(2023, 12, 1); // Fixed date in the past so 2024 dates are valid
    
    public DateTime Now => _fixedTime;
    
    public DateTime UtcNow => _fixedTime.ToUniversalTime();
    
    public DateOnly Today => DateOnly.FromDateTime(_fixedTime);
    
    public DateOnly UtcToday => DateOnly.FromDateTime(_fixedTime.ToUniversalTime());
    
    /// <summary>
    /// Set the fixed time for testing
    /// </summary>
    public void SetTime(DateTime time)
    {
        _fixedTime = time;
    }
    
    /// <summary>
    /// Set the fixed time using UTC
    /// </summary>
    public void SetUtcTime(DateTime utcTime)
    {
        _fixedTime = utcTime.ToLocalTime();
    }
    
    /// <summary>
    /// Advance time by the specified amount
    /// </summary>
    public void AdvanceTime(TimeSpan timespan)
    {
        _fixedTime = _fixedTime.Add(timespan);
    }
    
    /// <summary>
    /// Set time to a specific date at midnight
    /// </summary>
    public void SetDate(DateOnly date)
    {
        _fixedTime = date.ToDateTime(TimeOnly.MinValue);
    }
    
    /// <summary>
    /// Set time to next Monday at 9 AM (useful for testing weekly scheduling)
    /// </summary>
    public void SetToNextMondayAt9Am()
    {
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)_fixedTime.DayOfWeek + 7) % 7;
        if (daysUntilMonday == 0 && _fixedTime.TimeOfDay > TimeSpan.FromHours(9))
        {
            daysUntilMonday = 7; // If it's already Monday after 9 AM, go to next Monday
        }
        
        _fixedTime = _fixedTime.Date.AddDays(daysUntilMonday).AddHours(9);
    }
    
    /// <summary>
    /// Set time to next Tuesday at 12 PM (useful for testing leaderboard posting)
    /// </summary>
    public void SetToNextTuesdayAt12Pm()
    {
        var daysUntilTuesday = ((int)DayOfWeek.Tuesday - (int)_fixedTime.DayOfWeek + 7) % 7;
        if (daysUntilTuesday == 0 && _fixedTime.TimeOfDay > TimeSpan.FromHours(12))
        {
            daysUntilTuesday = 7; // If it's already Tuesday after 12 PM, go to next Tuesday
        }
        
        _fixedTime = _fixedTime.Date.AddDays(daysUntilTuesday).AddHours(12);
    }
} 