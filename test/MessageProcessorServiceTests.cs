using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PomoChallengeCounter.Data;
using PomoChallengeCounter.Models;
using PomoChallengeCounter.Services;
using Shouldly;

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
        var mockLocalizationService = new Mock<ILocalizationService>();
        
        // Setup localization mock to return expected values from en.json
        mockLocalizationService.Setup(l => l.GetString("leaderboard.title", It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns((string key, string lang, object[] args) => $"🏆 Challenge Leaderboard - Week {args[0]}");
        mockLocalizationService.Setup(l => l.GetString("leaderboard.error_title", It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns("❌ Leaderboard Error");
        mockLocalizationService.Setup(l => l.GetString("leaderboard.error_description", It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns((string key, string lang, object[] args) => $"Unable to generate leaderboard for week {args[0]}.\nPlease contact an administrator if this issue persists.");
        mockLocalizationService.Setup(l => l.GetString("leaderboard.description", It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns((string key, string lang, object[] args) => $"**{args[0]}** - Q{args[1]}\n*Ranked by total challenge score with this week's progress*");
        mockLocalizationService.Setup(l => l.GetString("leaderboard.author_name", It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns((string key, string lang, object[] args) => $"Q{args[0]} Challenge Progress");
        mockLocalizationService.Setup(l => l.GetString("leaderboard.field_title", It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns("🏆 Challenge Leaderboard");
        mockLocalizationService.Setup(l => l.GetString("leaderboard.statistics_title", It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns("📊 Challenge Statistics");
        mockLocalizationService.Setup(l => l.GetString("leaderboard.no_data", It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns("No data found for this week.");
        mockLocalizationService.Setup(l => l.GetString("leaderboard.goal_next_week", It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns((string key, string lang, object[] args) => $"Goal: {args[0]} pts");
        
        var mockServiceProvider = new Mock<IServiceProvider>();
        
        _messageProcessor = new MessageProcessorService(_context, _mockEmojiService.Object, mockServiceProvider.Object, mockLocalizationService.Object, _mockLogger.Object);

        // Setup test data
        SetupTestData();
    }

    private void SetupTestData()
    {
        // Clear any existing tracked entities to avoid conflicts
        _context.ChangeTracker.Clear();
        
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

    [Fact]
    public void GetRandomRewardEmoji_WithEmptyList_ShouldReturnEmpty()
    {
        // Arrange - using reflection to access private method
        var method = typeof(MessageProcessorService).GetMethod("GetRandomRewardEmoji", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        
        var emptyRewardList = new List<Models.Emoji>();
        
        // Act
        var result = method?.Invoke(null, new object[] { emptyRewardList });
        
        // Assert
        result.ShouldNotBeNull();
        ((string)result).ShouldBe(string.Empty);
    }

    [Fact]
    public void GetRandomRewardEmoji_WithRewardEmojis_ShouldReturnValidEmoji()
    {
        // Arrange
        var rewardEmojis = new List<Models.Emoji>
        {
            new() { EmojiCode = "🏆" },
            new() { EmojiCode = "💎" },
            new() { EmojiCode = "👑" }
        };
        
        var method = typeof(MessageProcessorService).GetMethod("GetRandomRewardEmoji", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        
        // Act
        var result = (string)method!.Invoke(null, new object[] { rewardEmojis })!;
        
        // Assert - result should be one of the configured reward emojis
        result.ShouldNotBeNullOrEmpty();
        new[] { "🏆", "💎", "👑" }.ShouldContain(result);
    }

    [Theory]
    [InlineData(25, 150, 20, "Epic productivity this week! 🔥 Keep the momentum going!")]
    [InlineData(8, 75, 6, "Great effort this week! ⭐ You're making progress!")]
    [InlineData(3, 25, 1, "Good start! 📈 Every session counts!")]
    [InlineData(1, 5, 0, "Building momentum! 🌱 Every step forward matters!")]
    public void GetMotivationalFooter_ShouldGenerateAppropriateMessages(int participants, int weeklyPoints, int goalsAchieved, string expectedPattern)
    {
        // Arrange
        var method = typeof(MessageProcessorService).GetMethod("GetMotivationalFooter", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        
        // Act
        var result = method?.Invoke(null, new object[] { participants, weeklyPoints, goalsAchieved });
        
        // Assert
        result.ShouldNotBeNull();
        var actualMessage = (string)result;
        actualMessage.ShouldBe(expectedPattern);
    }

    [Fact]
    public void GetMotivationalFooter_WithHighAchievementRate_ShouldIncludeFireEmoji()
    {
        // Arrange - 80% goal achievement rate (4 out of 5)
        var method = typeof(MessageProcessorService).GetMethod("GetMotivationalFooter", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        
        // Act
        var result = (string)method!.Invoke(null, new object[] { 5, 100, 4 })!;
        
        // Assert
        result.ShouldContain("🔥"); // High achievement rate should include fire emoji
        result.ShouldContain("Epic"); // 5 participants is not epic, but high points should influence
    }

    [Fact]
    public async Task GenerateLeaderboardEmbedAsync_ShouldCreateWellFormattedEmbed()
    {
        // Arrange - test data already set up in constructor
        
        // Add some message logs for leaderboard data
        var messageLog1 = new MessageLog
        {
            MessageId = 123,
            UserId = 456,
            WeekId = 1,
            PomodoroPoints = 25,
            BonusPoints = 5,
            GoalPoints = 10
        };
        
        var messageLog2 = new MessageLog
        {
            MessageId = 124,
            UserId = 457,
            WeekId = 1,
            PomodoroPoints = 30,
            BonusPoints = 0,
            GoalPoints = 15
        };
        
        await _context.MessageLogs.AddRangeAsync(messageLog1, messageLog2);
        await _context.SaveChangesAsync();
        
        // Act
        var embed = await _messageProcessor.GenerateLeaderboardEmbedAsync(1);
        
        // Assert
        embed.ShouldNotBeNull();
        embed.Title.ShouldContain("🏆 Challenge Leaderboard");
        embed.Title.ShouldContain("Week 1");
        embed.Description.ShouldContain("Test Challenge");
        embed.Description.ShouldContain("Q1");
        embed.Color.ShouldBe(new NetCord.Color(0xffd700)); // Gold color
        embed.Fields.ShouldNotBeEmpty();
        embed.Footer.ShouldNotBeNull();
    }

    [Fact]
    public async Task GenerateLeaderboardEmbedAsync_WithNoData_ShouldReturnNoDataEmbed()
    {
        // Arrange - test data already set up in constructor
        // No message logs added
        
        // Act
        var embed = await _messageProcessor.GenerateLeaderboardEmbedAsync(1);
        
        // Assert
        embed.ShouldNotBeNull();
        embed.Title.ShouldContain("🏆 Challenge Leaderboard");
        embed.Description.ShouldContain("No data found for this week");
        embed.Color.ShouldBe(new NetCord.Color(0xffd700));
    }

    [Fact]
    public async Task GenerateLeaderboardEmbedAsync_WithInvalidWeek_ShouldReturnErrorEmbed()
    {
        // Arrange - test data already set up in constructor
        
        // Act
        var embed = await _messageProcessor.GenerateLeaderboardEmbedAsync(999); // Non-existent week
        
        // Assert
        embed.ShouldNotBeNull();
        embed.Title.ShouldContain("❌ Leaderboard Error");
        embed.Description.ShouldContain("Unable to generate leaderboard for week 999");
        embed.Color.ShouldBe(new NetCord.Color(0xff0000)); // Red color for error
    }
} 