using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Shouldly;
using PomoChallengeCounter.Services;
using PomoChallengeCounter.Data;
using PomoChallengeCounter.Models;

namespace PomoChallengeCounter.Tests;

public class MessageHandlingServiceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly PomoChallengeDbContext _context;
    private readonly MessageProcessorService _messageProcessorService;

    public MessageHandlingServiceTests()
    {
        var services = new ServiceCollection();
        
        // Add in-memory database
        services.AddDbContext<PomoChallengeDbContext>(options =>
            options.UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}"));
            
        // Add required services
        services.AddSingleton<ITimeProvider, SystemTimeProvider>();
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<IEmojiService, EmojiService>();
        services.AddScoped<MessageProcessorService>();
        services.AddScoped<IChallengeService, ChallengeService>();
        
        // Add logging
        services.AddLogging(builder => builder.AddConsole());
        
        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<PomoChallengeDbContext>();
        _messageProcessorService = _serviceProvider.GetRequiredService<MessageProcessorService>();
    }

    [Fact]
    public async Task ProcessMessageAsync_ShouldSkipMessagesWithoutEmojis()
    {
        // Arrange
        await SetupActiveChallenge();
        
        // Act
        var result = await _messageProcessorService.ProcessMessageAsync(
            123, 456, "just a regular message without emojis", 789);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.Reason.ShouldContain("No emojis");
        
        var logs = await _context.MessageLogs.ToListAsync();
        logs.ShouldBeEmpty();
    }

    [Fact]
    public async Task ProcessMessageAsync_ShouldProcessTomatoEmojis()
    {
        // Arrange
        await SetupActiveChallenge();
        
        // Act
        var result = await _messageProcessorService.ProcessMessageAsync(
            123, 456, "🍅 completed a pomodoro!", 789);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.MessageLog.ShouldNotBeNull();
        result.MessageLog.PomodoroPoints.ShouldBe(1); // 1 tomato = 1 point
        
        var logs = await _context.MessageLogs.ToListAsync();
        logs.ShouldHaveSingleItem();
        logs[0].MessageId.ShouldBe(123ul);
        logs[0].UserId.ShouldBe(456ul);
        logs[0].PomodoroPoints.ShouldBe(1);
    }

    [Fact]
    public async Task ProcessMessageAsync_ShouldHandleMultipleTomatoEmojis()
    {
        // Arrange
        await SetupActiveChallenge();
        
        // Act
        var result = await _messageProcessorService.ProcessMessageAsync(
            124, 457, "🍅🍅🍅 three pomos done!", 789);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.MessageLog.ShouldNotBeNull();
        result.MessageLog.PomodoroPoints.ShouldBe(3); // 3 tomatoes = 3 points
        
        var logs = await _context.MessageLogs.ToListAsync();
        logs.ShouldHaveSingleItem();
        logs[0].PomodoroPoints.ShouldBe(3);
    }

    [Fact]
    public async Task ProcessMessageAsync_ShouldPreventDuplicateProcessing()
    {
        // Arrange
        await SetupActiveChallenge();
        var messageId = 125ul;
        var userId = 458ul;
        var content = "🍅 first time processing";
        
        // Act - process the same message twice
        var firstResult = await _messageProcessorService.ProcessMessageAsync(
            messageId, userId, content, 789);
        var secondResult = await _messageProcessorService.ProcessMessageAsync(
            messageId, userId, content, 789);

        // Assert
        firstResult.IsSuccess.ShouldBeTrue();
        secondResult.IsSuccess.ShouldBeFalse();
        secondResult.Reason.ShouldContain("Already processed");
        
        var logs = await _context.MessageLogs.ToListAsync();
        logs.ShouldHaveSingleItem(); // Only one log entry despite two attempts
    }

    [Fact]
    public async Task ProcessMessageAsync_ShouldReturnErrorWhenNoActiveWeek()
    {
        // Arrange - no active challenge setup
        
        // Act
        var result = await _messageProcessorService.ProcessMessageAsync(
            126, 459, "🍅 this should fail", 789);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.Reason.ShouldContain("No active week");
        
        var logs = await _context.MessageLogs.ToListAsync();
        logs.ShouldBeEmpty();
    }

    [Fact]
    public async Task ProcessMessageAsync_ShouldAllowForceReprocessing()
    {
        // Arrange
        await SetupActiveChallenge();
        var messageId = 127ul;
        var userId = 460ul;
        var originalContent = "🍅 original";
        var updatedContent = "🍅🍅 updated";
        
        // Process initially
        await _messageProcessorService.ProcessMessageAsync(messageId, userId, originalContent, 789);
        
        // Act - force reprocess with updated content
        var result = await _messageProcessorService.ProcessMessageAsync(
            messageId, userId, updatedContent, 789, forceReprocess: true);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.MessageLog.ShouldNotBeNull();
        result.MessageLog.PomodoroPoints.ShouldBe(2); // 2 tomatoes from updated content
        
        var logs = await _context.MessageLogs.ToListAsync();
        logs.ShouldHaveSingleItem(); // Still only one log entry (updated)
        logs[0].PomodoroPoints.ShouldBe(2); // Updated points
    }

    private async Task SetupActiveChallenge()
    {
        var server = new Server { Id = 999, Name = "Test Server" };
        await _context.Servers.AddAsync(server);

        var challenge = new Challenge 
        { 
            ServerId = 999,
            SemesterNumber = 1,
            Theme = "Test Challenge",
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(6)),
            WeekCount = 2,
            ChannelId = 789,
            IsCurrent = true,
            IsActive = true
        };
        await _context.Challenges.AddAsync(challenge);

        var week = new Week
        {
            Challenge = challenge,
            WeekNumber = 1,
            ThreadId = 789
        };
        await _context.Weeks.AddAsync(week);

        // Add default tomato emoji
        var emoji = new PomoChallengeCounter.Models.Emoji
        {
            ServerId = 999,
            EmojiCode = "🍅", 
            EmojiType = EmojiType.Pomodoro,
            PointValue = 1
        };
        await _context.Emojis.AddAsync(emoji);

        await _context.SaveChangesAsync();
    }

    public void Dispose()
    {
        _context?.Dispose();
        _serviceProvider?.Dispose();
    }
} 