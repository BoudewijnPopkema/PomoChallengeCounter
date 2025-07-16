using PomoChallengeCounter.Services;
using Shouldly;

namespace PomoChallengeCounter.Tests;

public class TimeProviderTests
{
    [Fact]
    public void SystemTimeProvider_ShouldReturnCurrentTime()
    {
        // Arrange
        var timeProvider = new SystemTimeProvider();
        var beforeCall = DateTime.Now;
        
        // Act
        var now = timeProvider.Now;
        var utcNow = timeProvider.UtcNow;
        var today = timeProvider.Today;
        var utcToday = timeProvider.UtcToday;
        
        var afterCall = DateTime.Now;
        
        // Assert
        now.ShouldBeInRange(beforeCall, afterCall);
        utcNow.ShouldBeInRange(beforeCall.ToUniversalTime(), afterCall.ToUniversalTime());
        today.ShouldBe(DateOnly.FromDateTime(beforeCall));
        utcToday.ShouldBe(DateOnly.FromDateTime(beforeCall.ToUniversalTime()));
    }

    [Fact]
    public void MockTimeProvider_SetTime_ShouldReturnFixedTime()
    {
        // Arrange
        var mockTime = new MockTimeProvider();
        var fixedTime = new DateTime(2024, 3, 15, 10, 30, 45);
        
        // Act
        mockTime.SetTime(fixedTime);
        
        // Assert
        mockTime.Now.ShouldBe(fixedTime);
        mockTime.UtcNow.ShouldBe(fixedTime.ToUniversalTime());
        mockTime.Today.ShouldBe(DateOnly.FromDateTime(fixedTime));
        mockTime.UtcToday.ShouldBe(DateOnly.FromDateTime(fixedTime.ToUniversalTime()));
    }

    [Fact]
    public void MockTimeProvider_SetUtcTime_ShouldConvertToLocal()
    {
        // Arrange
        var mockTime = new MockTimeProvider();
        var utcTime = new DateTime(2024, 3, 15, 14, 30, 45, DateTimeKind.Utc);
        
        // Act
        mockTime.SetUtcTime(utcTime);
        
        // Assert
        mockTime.UtcNow.ShouldBe(utcTime);
        mockTime.Now.ShouldBe(utcTime.ToLocalTime());
    }

    [Fact]
    public void MockTimeProvider_AdvanceTime_ShouldIncrementTime()
    {
        // Arrange
        var mockTime = new MockTimeProvider();
        var startTime = new DateTime(2024, 3, 15, 10, 0, 0);
        mockTime.SetTime(startTime);
        
        // Act
        mockTime.AdvanceTime(TimeSpan.FromHours(2));
        
        // Assert
        mockTime.Now.ShouldBe(startTime.AddHours(2));
    }

    [Fact]
    public void MockTimeProvider_SetDate_ShouldSetToMidnight()
    {
        // Arrange
        var mockTime = new MockTimeProvider();
        var targetDate = new DateOnly(2024, 3, 15);
        
        // Act
        mockTime.SetDate(targetDate);
        
        // Assert
        mockTime.Today.ShouldBe(targetDate);
        mockTime.Now.TimeOfDay.ShouldBe(TimeSpan.Zero); // Midnight
    }

    [Theory]
    [InlineData(DayOfWeek.Sunday, 1)] // Sunday -> Monday (1 day)
    [InlineData(DayOfWeek.Monday, 7)] // Monday after 9 AM -> next Monday (7 days)
    [InlineData(DayOfWeek.Tuesday, 6)] // Tuesday -> Monday (6 days)
    [InlineData(DayOfWeek.Wednesday, 5)] // Wednesday -> Monday (5 days)
    [InlineData(DayOfWeek.Thursday, 4)] // Thursday -> Monday (4 days)
    [InlineData(DayOfWeek.Friday, 3)] // Friday -> Monday (3 days)
    [InlineData(DayOfWeek.Saturday, 2)] // Saturday -> Monday (2 days)
    public void MockTimeProvider_SetToNextMondayAt9AM_ShouldCalculateCorrectly(DayOfWeek startDay, int expectedDaysToAdd)
    {
        // Arrange
        var mockTime = new MockTimeProvider();
        var startDate = GetDateForDayOfWeek(startDay);
        var startTime = startDate.AddHours(15); // 3 PM to ensure we're past 9 AM if it's Monday
        mockTime.SetTime(startTime);
        
        // Act
        mockTime.SetToNextMondayAt9Am();
        
        // Assert
        mockTime.Now.DayOfWeek.ShouldBe(DayOfWeek.Monday);
        mockTime.Now.Hour.ShouldBe(9);
        mockTime.Now.Minute.ShouldBe(0);
        mockTime.Now.Second.ShouldBe(0);
        
        var expectedDate = startDate.AddDays(expectedDaysToAdd);
        mockTime.Now.Date.ShouldBe(expectedDate.Date);
    }

    [Fact]
    public void MockTimeProvider_SetToNextMondayAt9AM_WhenMondayBefore9AM_ShouldStayOnSameMonday()
    {
        // Arrange
        var mockTime = new MockTimeProvider();
        var mondayAt8Am = GetDateForDayOfWeek(DayOfWeek.Monday).AddHours(8);
        mockTime.SetTime(mondayAt8Am);
        
        // Act
        mockTime.SetToNextMondayAt9Am();
        
        // Assert
        mockTime.Now.Date.ShouldBe(mondayAt8Am.Date); // Same day
        mockTime.Now.Hour.ShouldBe(9);
    }

    [Theory]
    [InlineData(DayOfWeek.Sunday, 2)] // Sunday -> Tuesday (2 days)
    [InlineData(DayOfWeek.Monday, 1)] // Monday -> Tuesday (1 day)
    [InlineData(DayOfWeek.Tuesday, 7)] // Tuesday after 12 PM -> next Tuesday (7 days)
    [InlineData(DayOfWeek.Wednesday, 6)] // Wednesday -> Tuesday (6 days)
    [InlineData(DayOfWeek.Thursday, 5)] // Thursday -> Tuesday (5 days)
    [InlineData(DayOfWeek.Friday, 4)] // Friday -> Tuesday (4 days)
    [InlineData(DayOfWeek.Saturday, 3)] // Saturday -> Tuesday (3 days)
    public void MockTimeProvider_SetToNextTuesdayAt12PM_ShouldCalculateCorrectly(DayOfWeek startDay, int expectedDaysToAdd)
    {
        // Arrange
        var mockTime = new MockTimeProvider();
        var startDate = GetDateForDayOfWeek(startDay);
        var startTime = startDate.AddHours(15); // 3 PM to ensure we're past 12 PM if it's Tuesday
        mockTime.SetTime(startTime);
        
        // Act
        mockTime.SetToNextTuesdayAt12Pm();
        
        // Assert
        mockTime.Now.DayOfWeek.ShouldBe(DayOfWeek.Tuesday);
        mockTime.Now.Hour.ShouldBe(12);
        mockTime.Now.Minute.ShouldBe(0);
        mockTime.Now.Second.ShouldBe(0);
        
        var expectedDate = startDate.AddDays(expectedDaysToAdd);
        mockTime.Now.Date.ShouldBe(expectedDate.Date);
    }

    [Fact]
    public void MockTimeProvider_SetToNextTuesdayAt12PM_WhenTuesdayBefore12PM_ShouldStayOnSameTuesday()
    {
        // Arrange
        var mockTime = new MockTimeProvider();
        var tuesdayAt10Am = GetDateForDayOfWeek(DayOfWeek.Tuesday).AddHours(10);
        mockTime.SetTime(tuesdayAt10Am);
        
        // Act
        mockTime.SetToNextTuesdayAt12Pm();
        
        // Assert
        mockTime.Now.Date.ShouldBe(tuesdayAt10Am.Date); // Same day
        mockTime.Now.Hour.ShouldBe(12);
    }

    [Fact]
    public void MockTimeProvider_MultipleTimeOperations_ShouldMaintainState()
    {
        // Arrange
        var mockTime = new MockTimeProvider();
        var startTime = new DateTime(2024, 3, 15, 10, 0, 0);
        mockTime.SetTime(startTime);
        
        // Act & Assert - Chain multiple operations
        mockTime.AdvanceTime(TimeSpan.FromHours(5));
        mockTime.Now.ShouldBe(startTime.AddHours(5));
        
        mockTime.AdvanceTime(TimeSpan.FromDays(1));
        mockTime.Now.ShouldBe(startTime.AddHours(5).AddDays(1));
        
        mockTime.SetToNextMondayAt9Am();
        mockTime.Now.DayOfWeek.ShouldBe(DayOfWeek.Monday);
        mockTime.Now.Hour.ShouldBe(9);
    }

    /// <summary>
    /// Helper method to get a DateTime for a specific day of week in the current week
    /// </summary>
    private static DateTime GetDateForDayOfWeek(DayOfWeek targetDay)
    {
        var today = DateTime.Today;
        var daysToAdd = ((int)targetDay - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(daysToAdd);
    }
} 