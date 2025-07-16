using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Shouldly;
using NetCord.Gateway;
using PomoChallengeCounter.Services;
using PomoChallengeCounter.Data;
using PomoChallengeCounter.Models;

namespace PomoChallengeCounter.Tests;

public class AutomationServiceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly PomoChallengeDbContext _context;
    private readonly MockTimeProvider _mockTimeProvider;
    private readonly AutomationService _automationService;

    public AutomationServiceTests()
    {
        var services = new ServiceCollection();
        
        // Use a fixed database name and ensure it's shared as singleton
        var databaseName = $"TestDb_{Guid.NewGuid()}";
        services.AddDbContext<PomoChallengeDbContext>(options =>
            options.UseInMemoryDatabase(databaseName), ServiceLifetime.Singleton);
            
        // Add required services
        _mockTimeProvider = new MockTimeProvider();
        services.AddSingleton<ITimeProvider>(_mockTimeProvider);
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<IEmojiService, EmojiService>();
        services.AddScoped<IChallengeService, ChallengeService>();
        services.AddLogging();
        
        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<PomoChallengeDbContext>();
        
        // GatewayClient not used in current implementation, pass null
        GatewayClient? mockGatewayClient = null;
        
        _automationService = new AutomationService(
            _serviceProvider,
            _mockTimeProvider,
            mockGatewayClient,
            _serviceProvider.GetRequiredService<LocalizationService>(),
            _serviceProvider.GetRequiredService<ILogger<AutomationService>>());
    }

    [Fact]
    public async Task AutomationService_ShouldNotCreateThreadsWhenNotMonday()
    {
        // Arrange
        await SetupActiveChallenge();
        _mockTimeProvider.SetTime(new DateTime(2024, 3, 19, 8, 5, 0, DateTimeKind.Utc)); // Tuesday at 8:05 AM UTC (9:05 AM Amsterdam)
        
        // Act
        await InvokeCheckAndCreateWeeklyThreadsAsync();
        
        // Assert
        var weeks = await _context.Weeks.ToListAsync();
        weeks.ShouldBeEmpty(); // No threads should be created on Tuesday
    }

    [Fact]
    public async Task AutomationService_ShouldNotCreateThreadsOutsideTimeWindow()
    {
        // Arrange
        await SetupActiveChallenge();
        _mockTimeProvider.SetTime(new DateTime(2024, 3, 18, 7, 30, 0, DateTimeKind.Utc)); // Monday at 7:30 AM UTC (8:30 AM Amsterdam - before window)
        
        // Act
        await InvokeCheckAndCreateWeeklyThreadsAsync();
        
        // Assert
        var weeks = await _context.Weeks.ToListAsync();
        weeks.ShouldBeEmpty(); // No threads should be created before 9 AM
    }

    [Fact]
    public async Task AutomationService_ShouldCreateThreadOnMondayAt9AM()
    {
        // Arrange
        await SetupActiveChallenge();
        _mockTimeProvider.SetTime(new DateTime(2024, 3, 18, 8, 5, 0, DateTimeKind.Utc)); // Monday at 8:05 AM UTC (9:05 AM Amsterdam - in window)
        
        // Act
        await InvokeCheckAndCreateWeeklyThreadsAsync();
        
        // Assert
        var weeks = await _context.Weeks.ToListAsync();
        weeks.ShouldHaveSingleItem();
        weeks[0].WeekNumber.ShouldBe(1); // First week
        weeks[0].ThreadId.ShouldBe(0ul); // Not set yet (pending Discord integration)
        weeks[0].LeaderboardPosted.ShouldBeFalse();
    }

    [Fact]
    public async Task AutomationService_ShouldNotCreateDuplicateThreads()
    {
        // Arrange
        await SetupActiveChallenge();
        _mockTimeProvider.SetTime(new DateTime(2024, 3, 18, 8, 5, 0, DateTimeKind.Utc)); // Monday at 8:05 AM UTC (9:05 AM Amsterdam)
        
        // Act - run twice
        await InvokeCheckAndCreateWeeklyThreadsAsync();
        await InvokeCheckAndCreateWeeklyThreadsAsync();
        
        // Assert
        var weeks = await _context.Weeks.ToListAsync();
        weeks.ShouldHaveSingleItem(); // Only one thread should be created
        weeks[0].WeekNumber.ShouldBe(1);
    }

    [Fact]
    public async Task AutomationService_ShouldCreateSecondWeekThread()
    {
        // Arrange
        await SetupActiveChallenge();
        
        // Create first week
        _mockTimeProvider.SetTime(new DateTime(2024, 3, 18, 8, 5, 0, DateTimeKind.Utc)); // Monday week 1 (8:05 UTC = 9:05 Amsterdam)
        await InvokeCheckAndCreateWeeklyThreadsAsync();
        
        // Move to second week
        _mockTimeProvider.SetTime(new DateTime(2024, 3, 25, 8, 5, 0, DateTimeKind.Utc)); // Monday week 2 (8:05 UTC = 9:05 Amsterdam)
        
        // Act
        await InvokeCheckAndCreateWeeklyThreadsAsync();
        
        // Assert
        var weeks = await _context.Weeks.OrderBy(w => w.WeekNumber).ToListAsync();
        weeks.Count.ShouldBe(2);
        weeks[0].WeekNumber.ShouldBe(1);
        weeks[1].WeekNumber.ShouldBe(2);
    }

    [Fact]
    public async Task AutomationService_ShouldNotCreateThreadBeyondChallengeEnd()
    {
        // Arrange
        await SetupActiveChallenge(weekCount: 2); // 2-week challenge
        
        // Move to week 3 (beyond challenge)
        _mockTimeProvider.SetTime(new DateTime(2024, 4, 1, 7, 5, 0, DateTimeKind.Utc)); // Monday week 3 (7:05 UTC = 9:05 Amsterdam CEST)
        
        // Act
        await InvokeCheckAndCreateWeeklyThreadsAsync();
        
        // Assert
        var weeks = await _context.Weeks.ToListAsync();
        weeks.ShouldBeEmpty(); // No threads should be created beyond challenge duration
    }

    [Fact]
    public async Task AutomationService_ShouldNotCreateThreadForInactiveChallenge()
    {
        // Arrange
        await SetupActiveChallenge(isActive: false);
        _mockTimeProvider.SetTime(new DateTime(2024, 3, 18, 8, 5, 0, DateTimeKind.Utc)); // Monday at 8:05 AM UTC (9:05 AM Amsterdam)
        
        // Act
        await InvokeCheckAndCreateWeeklyThreadsAsync();
        
        // Assert
        var weeks = await _context.Weeks.ToListAsync();
        weeks.ShouldBeEmpty(); // No threads for inactive challenges
    }

    [Fact]
    public async Task AutomationService_ShouldHandleMultipleChallenges()
    {
        // Arrange
        await SetupActiveChallenge(serverId: 111, challengeId: 1);
        await SetupActiveChallenge(serverId: 222, challengeId: 2);
        _mockTimeProvider.SetTime(new DateTime(2024, 3, 18, 8, 5, 0, DateTimeKind.Utc)); // Monday at 8:05 AM UTC (9:05 AM Amsterdam)
        
        // Act
        await InvokeCheckAndCreateWeeklyThreadsAsync();
        
        // Assert
        var weeks = await _context.Weeks.ToListAsync();
        weeks.Count.ShouldBe(2); // One thread per challenge
        weeks.All(w => w.WeekNumber == 1).ShouldBeTrue();
    }

    [Fact]
    public async Task AutomationService_ShouldSkipChallengesNotInCurrentTimeframe()
    {
        // Arrange - Challenge that hasn't started yet
        await SetupActiveChallenge(
            startDate: new DateOnly(2024, 4, 1), // Future start
            endDate: new DateOnly(2024, 5, 6));
        
        _mockTimeProvider.SetTime(new DateTime(2024, 3, 18, 8, 5, 0, DateTimeKind.Utc)); // Before challenge start (8:05 UTC = 9:05 Amsterdam)
        
        // Act
        await InvokeCheckAndCreateWeeklyThreadsAsync();
        
        // Assert
        var weeks = await _context.Weeks.ToListAsync();
        weeks.ShouldBeEmpty(); // No threads for future challenges
    }

    [Fact]
    public async Task AutomationService_ShouldPostLeaderboardOnTuesdayAt12PM()
    {
        // Arrange
        await SetupActiveChallenge();
        
        // Create a week that needs leaderboard (simulate week has ended)
        var week = new Week
        {
            ChallengeId = 1,
            WeekNumber = 1,
            ThreadId = 12345, // Has thread
            LeaderboardPosted = false
        };
        await _context.Weeks.AddAsync(week);
        await _context.SaveChangesAsync();
        
        // Set time to Tuesday March 26 12:05 PM UTC (1:05 PM Amsterdam CET) - after week 1 ends
        _mockTimeProvider.SetTime(new DateTime(2024, 3, 26, 11, 5, 0, DateTimeKind.Utc));
        
        // Act
        await InvokeCheckAndPostLeaderboardsAsync();
        
        // Assert
        var updatedWeek = await _context.Weeks.FindAsync(week.Id);
        updatedWeek.ShouldNotBeNull();
        updatedWeek.LeaderboardPosted.ShouldBeTrue();
    }

    [Fact]
    public async Task AutomationService_ShouldNotPostLeaderboardOnWrongDay()
    {
        // Arrange
        await SetupActiveChallenge();
        
        var week = new Week
        {
            ChallengeId = 1,
            WeekNumber = 1,
            ThreadId = 12345,
            LeaderboardPosted = false
        };
        await _context.Weeks.AddAsync(week);
        await _context.SaveChangesAsync();
        
        // Set time to Wednesday 12:05 PM UTC
        _mockTimeProvider.SetTime(new DateTime(2024, 3, 20, 11, 5, 0, DateTimeKind.Utc));
        
        // Act
        await InvokeCheckAndPostLeaderboardsAsync();
        
        // Assert
        var updatedWeek = await _context.Weeks.FindAsync(week.Id);
        updatedWeek.ShouldNotBeNull();
        updatedWeek.LeaderboardPosted.ShouldBeFalse(); // Should not be posted on wrong day
    }

    [Fact]
    public async Task AutomationService_ShouldNotPostLeaderboardOutsideTimeWindow()
    {
        // Arrange
        await SetupActiveChallenge();
        
        var week = new Week
        {
            ChallengeId = 1,
            WeekNumber = 1,
            ThreadId = 12345,
            LeaderboardPosted = false
        };
        await _context.Weeks.AddAsync(week);
        await _context.SaveChangesAsync();
        
        // Set time to Tuesday March 26 10:30 AM UTC (11:30 AM Amsterdam - outside window, but after week ends)
        _mockTimeProvider.SetTime(new DateTime(2024, 3, 26, 10, 30, 0, DateTimeKind.Utc));
        
        // Act
        await InvokeCheckAndPostLeaderboardsAsync();
        
        // Assert
        var updatedWeek = await _context.Weeks.FindAsync(week.Id);
        updatedWeek.ShouldNotBeNull();
        updatedWeek.LeaderboardPosted.ShouldBeFalse(); // Should not be posted outside time window
    }

    [Fact]
    public async Task AutomationService_ShouldNotPostLeaderboardForWeekWithoutThread()
    {
        // Arrange
        await SetupActiveChallenge();
        
        var week = new Week
        {
            ChallengeId = 1,
            WeekNumber = 1,
            ThreadId = 0, // No thread created yet
            LeaderboardPosted = false
        };
        await _context.Weeks.AddAsync(week);
        await _context.SaveChangesAsync();
        
        // Set time to Tuesday March 26 12:05 PM UTC (after week 1 ends)
        _mockTimeProvider.SetTime(new DateTime(2024, 3, 26, 11, 5, 0, DateTimeKind.Utc));
        
        // Act
        await InvokeCheckAndPostLeaderboardsAsync();
        
        // Assert
        var updatedWeek = await _context.Weeks.FindAsync(week.Id);
        updatedWeek.ShouldNotBeNull();
        updatedWeek.LeaderboardPosted.ShouldBeFalse(); // Should not post without thread
    }

    [Fact]
    public async Task AutomationService_ShouldNotPostLeaderboardTwice()
    {
        // Arrange
        await SetupActiveChallenge();
        
        var week = new Week
        {
            ChallengeId = 1,
            WeekNumber = 1,
            ThreadId = 12345,
            LeaderboardPosted = true // Already posted
        };
        await _context.Weeks.AddAsync(week);
        await _context.SaveChangesAsync();
        
        // Set time to Tuesday March 26 12:05 PM UTC (after week 1 ends)
        _mockTimeProvider.SetTime(new DateTime(2024, 3, 26, 11, 5, 0, DateTimeKind.Utc));
        
        // Act
        await InvokeCheckAndPostLeaderboardsAsync();
        
        // Assert - should remain true (no change)
        var updatedWeek = await _context.Weeks.FindAsync(week.Id);
        updatedWeek.ShouldNotBeNull();
        updatedWeek.LeaderboardPosted.ShouldBeTrue();
    }

    private async Task SetupActiveChallenge(
        ulong serverId = 999, 
        int challengeId = 1,
        DateOnly? startDate = null, 
        DateOnly? endDate = null,
        int weekCount = 4,
        bool isActive = true,
        bool isCurrent = true)
    {
        var server = new Server { Id = serverId, Name = "Test Server" };
        await _context.Servers.AddAsync(server);

        var challenge = new Challenge 
        { 
            Id = challengeId,
            ServerId = serverId,
            SemesterNumber = 1,
            Theme = "Test Challenge",
            StartDate = startDate ?? new DateOnly(2024, 3, 18), // Monday
            EndDate = endDate ?? new DateOnly(2024, 4, 14),     // Sunday (4 weeks later)
            WeekCount = weekCount,
            ChannelId = 789,
            IsCurrent = isCurrent,
            IsActive = isActive
        };
        await _context.Challenges.AddAsync(challenge);
        await _context.SaveChangesAsync();
    }

    private async Task InvokeCheckAndCreateWeeklyThreadsAsync()
    {
        // Use reflection to call the private method for testing
        var method = typeof(AutomationService).GetMethod("CheckAndCreateWeeklyThreadsAsync", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method.ShouldNotBeNull();
        
        var task = (Task)method.Invoke(_automationService, Array.Empty<object>());
        await task;
    }

    private async Task InvokeCheckAndPostLeaderboardsAsync()
    {
        var automationService = new AutomationService(
            _serviceProvider,
            _mockTimeProvider,
            null, // GatewayClient not used in current implementation
            _serviceProvider.GetRequiredService<LocalizationService>(),
            _serviceProvider.GetRequiredService<ILogger<AutomationService>>());

        // Use reflection to call the private method
        var method = typeof(AutomationService).GetMethod("CheckAndPostLeaderboardsAsync", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method.ShouldNotBeNull();
        
        var task = (Task)method.Invoke(automationService, null);
        task.ShouldNotBeNull();
        await task;
    }

    public void Dispose()
    {
        _context?.Dispose();
        _serviceProvider?.Dispose();
    }
} 