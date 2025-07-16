using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PomoChallengeCounter.Data;
using PomoChallengeCounter.Models;
using PomoChallengeCounter.Models.Results;
using PomoChallengeCounter.Services;
using Shouldly;
using Xunit;

namespace PomoChallengeCounter.Tests;

public class MessageProcessorServiceTests : IDisposable
{
    private readonly PomoChallengeDbContext _context;
    private readonly Mock<IEmojiService> _mockEmojiService;
    private readonly Mock<ILogger<MessageProcessorService>> _mockLogger;
    private readonly MessageProcessorService _messageProcessor;
    
    private Server _testServer = null!;
    private Challenge _testChallenge = null!;
    private Week _testWeek = null!;

    public MessageProcessorServiceTests()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<PomoChallengeDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new PomoChallengeDbContext(options);

        // Setup mocks
        _mockEmojiService = new Mock<IEmojiService>();
        _mockLogger = new Mock<ILogger<MessageProcessorService>>();
        
        _messageProcessor = new MessageProcessorService(_context, _mockEmojiService.Object, _mockLogger.Object);

        // Setup test data
        SetupTestData();
    }

    private void SetupTestData()
    {
        _testServer = new Server
        {
            Id = 123456789,
            Name = "Test Server",
            Language = "en"
        };

        _testChallenge = new Challenge
        {
            ServerId = _testServer.Id,
            SemesterNumber = 1,
            Theme = "Test Challenge Theme",
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            WeekCount = 2,
            IsActive = true,
            IsCurrent = true
        };

        _testWeek = new Week
        {
            ChallengeId = _testChallenge.Id,
            WeekNumber = 1,
            ThreadId = 987654321,
            Challenge = _testChallenge
        };

        _context.Servers.Add(_testServer);
        _context.Challenges.Add(_testChallenge);
        _context.Weeks.Add(_testWeek);
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async Task ProcessMessageAsync_WithNewMessage_ShouldCreateMessageLog()
    {
        // Arrange
        const ulong messageId = 111111111;
        const ulong userId = 222222222;
        const string messageContent = "I studied 📚 for 2 hours!";
        
        var emojiDetection = new EmojiDetectionResult();
        emojiDetection.UnicodeEmojis.Add("📚");
        
        _mockEmojiService.Setup(x => x.DetectEmojis(messageContent))
            .Returns(emojiDetection);

        // Setup emoji in database
        var emoji = new Emoji
        {
            ServerId = _testServer.Id,
            EmojiCode = "📚",
            EmojiType = EmojiType.Pomodoro,
            PointValue = 25,
            IsActive = true
        };
        _context.Emojis.Add(emoji);
        await _context.SaveChangesAsync();

        // Act - use the challenge thread ID
        var result = await _messageProcessor.ProcessMessageAsync(messageId, userId, messageContent, _testWeek.ThreadId);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.MessageLog.ShouldNotBeNull();
        result.MessageLog!.MessageId.ShouldBe(messageId);
        result.MessageLog.UserId.ShouldBe(userId);
        result.MessageLog.PomodoroPoints.ShouldBe(25);
        result.MessageLog.BonusPoints.ShouldBe(0);
        result.MessageLog.GoalPoints.ShouldBe(0);
        result.DetectedEmojis.ShouldBe(1);

        // Verify database
        var messageLog = await _context.MessageLogs.FirstAsync(ml => ml.MessageId == messageId);
        messageLog.ShouldNotBeNull();
        messageLog.PomodoroPoints.ShouldBe(25);
    }

    [Fact]
    public async Task ProcessMessageAsync_WithMixedEmojis_ShouldCalculateAllPoints()
    {
        // Arrange
        const ulong messageId = 333333333;
        const ulong userId = 444444444;
        const string messageContent = "Study session: 📚 :tomato: <:fire:123> complete!";
        
        var emojiDetection = new EmojiDetectionResult();
        emojiDetection.UnicodeEmojis.Add("📚");
        emojiDetection.ShortcodeEmojis.Add(":tomato:");
        emojiDetection.CustomEmojis.Add("<:fire:123>");
        
        _mockEmojiService.Setup(x => x.DetectEmojis(messageContent))
            .Returns(emojiDetection);

        // Setup emojis in database
        var emojis = new[]
        {
            new Emoji { ServerId = _testServer.Id, EmojiCode = "📚", EmojiType = EmojiType.Pomodoro, PointValue = 25, IsActive = true },
            new Emoji { ServerId = _testServer.Id, EmojiCode = ":tomato:", EmojiType = EmojiType.Bonus, PointValue = 5, IsActive = true },
            new Emoji { ServerId = _testServer.Id, EmojiCode = "<:fire:123>", EmojiType = EmojiType.Goal, PointValue = 10, IsActive = true }
        };
        _context.Emojis.AddRange(emojis);
        await _context.SaveChangesAsync();

        // Act - use the challenge thread ID
        var result = await _messageProcessor.ProcessMessageAsync(messageId, userId, messageContent, _testWeek.ThreadId);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.MessageLog!.PomodoroPoints.ShouldBe(25);
        result.MessageLog.BonusPoints.ShouldBe(5);
        result.MessageLog.GoalPoints.ShouldBe(10);
        result.DetectedEmojis.ShouldBe(3);
    }

    [Fact]
    public async Task ProcessMessageAsync_WithNoEmojis_ShouldReturnFailure()
    {
        // Arrange
        const ulong messageId = 555555555;
        const ulong userId = 666666666;
        const string messageContent = "Just text without emojis";
        
        var emojiDetection = new EmojiDetectionResult(); // Empty result
        
        _mockEmojiService.Setup(x => x.DetectEmojis(messageContent))
            .Returns(emojiDetection);

        // Act - no channel ID provided
        var result = await _messageProcessor.ProcessMessageAsync(messageId, userId, messageContent);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.Reason.ShouldBe("No active week");
        result.MessageLog.ShouldBeNull();
    }

    [Fact]
    public async Task ProcessMessageAsync_WithAlreadyProcessedMessage_ShouldSkip()
    {
        // Arrange
        const ulong messageId = 777777777;
        const ulong userId = 888888888;
        const string messageContent = "Test message 📚";
        
        // Create existing message log
        var existingLog = new MessageLog
        {
            MessageId = messageId,
            UserId = userId,
            WeekId = _testWeek.Id,
            PomodoroPoints = 25,
            BonusPoints = 0,
            GoalPoints = 0
        };
        _context.MessageLogs.Add(existingLog);
        await _context.SaveChangesAsync();

        // Act
        var result = await _messageProcessor.ProcessMessageAsync(messageId, userId, messageContent);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.Reason.ShouldBe("Already processed");
        result.MessageLog.ShouldBeNull();
    }

    [Fact]
    public async Task ProcessMessageAsync_WithForceReprocess_ShouldUpdateExisting()
    {
        // Arrange
        const ulong messageId = 999999999;
        const ulong userId = 101010101;
        const string messageContent = "Updated message 📚 :tomato:";
        
        // Create existing message log
        var existingLog = new MessageLog
        {
            MessageId = messageId,
            UserId = userId,
            WeekId = _testWeek.Id,
            PomodoroPoints = 25,
            BonusPoints = 0,
            GoalPoints = 0
        };
        _context.MessageLogs.Add(existingLog);
        await _context.SaveChangesAsync();

        var emojiDetection = new EmojiDetectionResult();
        emojiDetection.UnicodeEmojis.Add("📚");
        emojiDetection.ShortcodeEmojis.Add(":tomato:");
        
        _mockEmojiService.Setup(x => x.DetectEmojis(messageContent))
            .Returns(emojiDetection);

        // Setup emojis
        var emojis = new[]
        {
            new Emoji { ServerId = _testServer.Id, EmojiCode = "📚", EmojiType = EmojiType.Pomodoro, PointValue = 25, IsActive = true },
            new Emoji { ServerId = _testServer.Id, EmojiCode = ":tomato:", EmojiType = EmojiType.Bonus, PointValue = 5, IsActive = true }
        };
        _context.Emojis.AddRange(emojis);
        await _context.SaveChangesAsync();

        // Act - use the challenge thread ID
        var result = await _messageProcessor.ProcessMessageAsync(messageId, userId, messageContent, _testWeek.ThreadId, forceReprocess: true);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.MessageLog!.MessageId.ShouldBe(messageId);
        result.MessageLog.PomodoroPoints.ShouldBe(25);
        result.MessageLog.BonusPoints.ShouldBe(5);
        result.MessageLog.GoalPoints.ShouldBe(0);
    }

    [Theory]
    [InlineData(EmojiType.Pomodoro, 25, 25, 0, 0)]
    [InlineData(EmojiType.Bonus, 5, 0, 5, 0)]
    [InlineData(EmojiType.Goal, 10, 0, 0, 10)]
    [InlineData(EmojiType.Reward, 0, 0, 0, 0)] // Reward emojis don't contribute points
    public async Task ProcessMessageAsync_WithDifferentEmojiTypes_ShouldCalculateCorrectPoints(
        EmojiType emojiType, int pointValue, int expectedPomodoro, int expectedBonus, int expectedGoal)
    {
        // Arrange
        var messageId = (ulong)Random.Shared.Next(1000000, 9999999);
        var userId = (ulong)Random.Shared.Next(1000000, 9999999);
        const string messageContent = "Test emoji 📚";
        
        var emojiDetection = new EmojiDetectionResult();
        emojiDetection.UnicodeEmojis.Add("📚");
        
        _mockEmojiService.Setup(x => x.DetectEmojis(messageContent))
            .Returns(emojiDetection);

        var emoji = new Emoji
        {
            ServerId = _testServer.Id,
            EmojiCode = "📚",
            EmojiType = emojiType,
            PointValue = pointValue,
            IsActive = true
        };
        _context.Emojis.Add(emoji);
        await _context.SaveChangesAsync();

        // Act - use the challenge thread ID
        var result = await _messageProcessor.ProcessMessageAsync(messageId, userId, messageContent, _testWeek.ThreadId);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.MessageLog!.PomodoroPoints.ShouldBe(expectedPomodoro);
        result.MessageLog.BonusPoints.ShouldBe(expectedBonus);
        result.MessageLog.GoalPoints.ShouldBe(expectedGoal);
    }

    [Fact]
    public async Task ProcessMessageAsync_WithInactiveEmoji_ShouldNotCalculatePoints()
    {
        // Arrange
        const ulong messageId = 121212121;
        const ulong userId = 131313131;
        const string messageContent = "Test inactive emoji 📚";
        
        var emojiDetection = new EmojiDetectionResult();
        emojiDetection.UnicodeEmojis.Add("📚");
        
        _mockEmojiService.Setup(x => x.DetectEmojis(messageContent))
            .Returns(emojiDetection);

        var inactiveEmoji = new Emoji
        {
            ServerId = _testServer.Id,
            EmojiCode = "📚",
            EmojiType = EmojiType.Pomodoro,
            PointValue = 25,
            IsActive = false // Inactive emoji
        };
        _context.Emojis.Add(inactiveEmoji);
        await _context.SaveChangesAsync();

        // Act - use the challenge thread ID
        var result = await _messageProcessor.ProcessMessageAsync(messageId, userId, messageContent, _testWeek.ThreadId);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.MessageLog!.PomodoroPoints.ShouldBe(0);
        result.MessageLog.BonusPoints.ShouldBe(0);
        result.MessageLog.GoalPoints.ShouldBe(0);
    }

    [Fact]
    public async Task UpdateMessageAsync_WithExistingMessage_ShouldUpdatePoints()
    {
        // Arrange
        const ulong messageId = 141414141;
        const ulong userId = 151515151;
        const string newContent = "Updated with more emojis 📚 :tomato:";
        
        // Create existing message log
        var existingLog = new MessageLog
        {
            MessageId = messageId,
            UserId = userId,
            WeekId = _testWeek.Id,
            PomodoroPoints = 25,
            BonusPoints = 0,
            GoalPoints = 0
        };
        _context.MessageLogs.Add(existingLog);
        await _context.SaveChangesAsync();

        var emojiDetection = new EmojiDetectionResult();
        emojiDetection.UnicodeEmojis.Add("📚");
        emojiDetection.ShortcodeEmojis.Add(":tomato:");
        
        _mockEmojiService.Setup(x => x.DetectEmojis(newContent))
            .Returns(emojiDetection);

        // Setup emojis
        var emojis = new[]
        {
            new Emoji { ServerId = _testServer.Id, EmojiCode = "📚", EmojiType = EmojiType.Pomodoro, PointValue = 25, IsActive = true },
            new Emoji { ServerId = _testServer.Id, EmojiCode = ":tomato:", EmojiType = EmojiType.Bonus, PointValue = 5, IsActive = true }
        };
        _context.Emojis.AddRange(emojis);
        await _context.SaveChangesAsync();

        // Act
        var result = await _messageProcessor.UpdateMessageAsync(messageId, newContent);

        // Assert
        result.ShouldBeTrue();
        
        var updatedLog = await _context.MessageLogs.FirstAsync(ml => ml.MessageId == messageId);
        updatedLog.PomodoroPoints.ShouldBe(25);
        updatedLog.BonusPoints.ShouldBe(5);
    }

    [Fact]
    public async Task UpdateMessageAsync_WithNonExistentMessage_ShouldReturnFalse()
    {
        // Arrange
        const ulong messageId = 161616161;
        const string newContent = "New content 📚";

        // Act
        var result = await _messageProcessor.UpdateMessageAsync(messageId, newContent);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteMessageAsync_WithExistingMessage_ShouldDeleteLog()
    {
        // Arrange
        const ulong messageId = 171717171;
        const ulong userId = 181818181;
        
        var messageLog = new MessageLog
        {
            MessageId = messageId,
            UserId = userId,
            WeekId = _testWeek.Id,
            PomodoroPoints = 25,
            BonusPoints = 0,
            GoalPoints = 0
        };
        _context.MessageLogs.Add(messageLog);
        await _context.SaveChangesAsync();

        // Act
        var result = await _messageProcessor.DeleteMessageAsync(messageId);

        // Assert
        result.ShouldBeTrue();
        
        var deletedLog = await _context.MessageLogs.FirstOrDefaultAsync(ml => ml.MessageId == messageId);
        deletedLog.ShouldBeNull();
    }

    [Fact]
    public async Task DeleteMessageAsync_WithNonExistentMessage_ShouldReturnFalse()
    {
        // Arrange
        const ulong messageId = 191919191;

        // Act
        var result = await _messageProcessor.DeleteMessageAsync(messageId);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task RescanWeekAsync_WithMultipleMessages_ShouldProcessAll()
    {
        // Arrange
        // Set up testWeek with a proper ThreadId for week detection
        _testWeek.ThreadId = 555555555;
        await _context.SaveChangesAsync();
        
        var messages = new List<(ulong MessageId, ulong UserId, string Content)>
        {
            (201010101, 202020202, "Message 1 📚"),
            (203030303, 204040404, "Message 2 📚 :tomato:"),
            (205050505, 206060606, "Message 3 :fire:")
        };

        var emojiDetection1 = new EmojiDetectionResult();
        emojiDetection1.UnicodeEmojis.Add("📚");
        
        var emojiDetection2 = new EmojiDetectionResult();
        emojiDetection2.UnicodeEmojis.Add("📚");
        emojiDetection2.ShortcodeEmojis.Add(":tomato:");
        
        var emojiDetection3 = new EmojiDetectionResult();
        emojiDetection3.ShortcodeEmojis.Add(":fire:");

        _mockEmojiService.Setup(x => x.DetectEmojis("Message 1 📚")).Returns(emojiDetection1);
        _mockEmojiService.Setup(x => x.DetectEmojis("Message 2 📚 :tomato:")).Returns(emojiDetection2);
        _mockEmojiService.Setup(x => x.DetectEmojis("Message 3 :fire:")).Returns(emojiDetection3);

        // Setup emojis
        var emojis = new[]
        {
            new Emoji { ServerId = _testServer.Id, EmojiCode = "📚", EmojiType = EmojiType.Pomodoro, PointValue = 25, IsActive = true },
            new Emoji { ServerId = _testServer.Id, EmojiCode = ":tomato:", EmojiType = EmojiType.Bonus, PointValue = 5, IsActive = true },
            new Emoji { ServerId = _testServer.Id, EmojiCode = ":fire:", EmojiType = EmojiType.Goal, PointValue = 10, IsActive = true }
        };
        _context.Emojis.AddRange(emojis);
        await _context.SaveChangesAsync();

        // Act
        var result = await _messageProcessor.RescanWeekAsync(_testWeek.Id, messages);

        // Assert
        result.ShouldNotBeNull();
        
        var messageLogs = await _context.MessageLogs.Where(ml => ml.WeekId == _testWeek.Id).ToListAsync();
        messageLogs.Count.ShouldBe(3);
        
        var log1 = messageLogs.First(ml => ml.MessageId == 201010101);
        log1.PomodoroPoints.ShouldBe(25);
        
        var log2 = messageLogs.First(ml => ml.MessageId == 203030303);
        log2.PomodoroPoints.ShouldBe(25);
        log2.BonusPoints.ShouldBe(5);
        
        var log3 = messageLogs.First(ml => ml.MessageId == 205050505);
        log3.GoalPoints.ShouldBe(10);
    }

    [Fact]
    public async Task ProcessMessageAsync_WithMessageOutsideChallengeThread_ShouldIgnore()
    {
        // Arrange
        const ulong messageId = 999999999;
        const ulong userId = 888888888;
        const string messageContent = "Random message with 📚 emoji";
        const ulong randomChannelId = 123123123; // Not a challenge thread
        
        var emojiDetection = new EmojiDetectionResult();
        emojiDetection.UnicodeEmojis.Add("📚");
        
        _mockEmojiService.Setup(x => x.DetectEmojis(messageContent))
            .Returns(emojiDetection);

        // Act
        var result = await _messageProcessor.ProcessMessageAsync(messageId, userId, messageContent, randomChannelId);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.Reason.ShouldBe("No active week");
        result.MessageLog.ShouldBeNull();
    }

    [Fact]
    public async Task ProcessMessageAsync_WithNoChannelId_ShouldIgnore()
    {
        // Arrange
        const ulong messageId = 888888888;
        const ulong userId = 777777777;
        const string messageContent = "Message with 📚 emoji but no channel";
        
        var emojiDetection = new EmojiDetectionResult();
        emojiDetection.UnicodeEmojis.Add("📚");
        
        _mockEmojiService.Setup(x => x.DetectEmojis(messageContent))
            .Returns(emojiDetection);

        // Act
        var result = await _messageProcessor.ProcessMessageAsync(messageId, userId, messageContent, null);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.Reason.ShouldBe("No active week");
        result.MessageLog.ShouldBeNull();
    }

    [Fact]
    public async Task ProcessMessageAsync_WithValidChallengeThread_ShouldProcess()
    {
        // Arrange
        const ulong messageId = 777777777;
        const ulong userId = 666666666;
        const string messageContent = "Study session 📚";
        
        // Update test week to have a specific thread ID
        _testWeek.ThreadId = 555555555;
        await _context.SaveChangesAsync();
        
        var emojiDetection = new EmojiDetectionResult();
        emojiDetection.UnicodeEmojis.Add("📚");
        
        _mockEmojiService.Setup(x => x.DetectEmojis(messageContent))
            .Returns(emojiDetection);

        // Setup emoji in database
        var emoji = new Emoji
        {
            ServerId = _testServer.Id,
            EmojiCode = "📚",
            EmojiType = EmojiType.Pomodoro,
            PointValue = 25,
            IsActive = true
        };
        _context.Emojis.Add(emoji);
        await _context.SaveChangesAsync();

        // Act - use the thread ID from the test week
        var result = await _messageProcessor.ProcessMessageAsync(messageId, userId, messageContent, _testWeek.ThreadId);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.MessageLog.ShouldNotBeNull();
        result.MessageLog!.PomodoroPoints.ShouldBe(25);
    }

    [Fact]
    public async Task ProcessMessageAsync_WithGoalThread_ShouldProcess()
    {
        // Arrange
        const ulong messageId = 666666666;
        const ulong userId = 555555555;
        const string messageContent = "Goal setting 🎯";
        
        // Update test week to have a goal thread ID
        _testWeek.GoalThreadId = 444444444;
        await _context.SaveChangesAsync();
        
        var emojiDetection = new EmojiDetectionResult();
        emojiDetection.UnicodeEmojis.Add("🎯");
        
        _mockEmojiService.Setup(x => x.DetectEmojis(messageContent))
            .Returns(emojiDetection);

        // Setup emoji in database
        var emoji = new Emoji
        {
            ServerId = _testServer.Id,
            EmojiCode = "🎯",
            EmojiType = EmojiType.Goal,
            PointValue = 10,
            IsActive = true
        };
        _context.Emojis.Add(emoji);
        await _context.SaveChangesAsync();

        // Act - use the goal thread ID
        var result = await _messageProcessor.ProcessMessageAsync(messageId, userId, messageContent, _testWeek.GoalThreadId);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.MessageLog.ShouldNotBeNull();
        result.MessageLog!.GoalPoints.ShouldBe(10);
    }
} 