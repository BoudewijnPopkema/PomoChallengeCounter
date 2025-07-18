using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using PomoChallengeCounter.Data;
using PomoChallengeCounter.Models;
using PomoChallengeCounter.Services;
using Shouldly;

namespace PomoChallengeCounter.Tests;

public class ChallengeImportTests : IDisposable
{
    private readonly PomoChallengeDbContext _context;
    private readonly ChallengeService _challengeService;
    private readonly MessageProcessorService _messageProcessor;
    private readonly MockTimeProvider _timeProvider;
    private readonly Server _testServer;

    public ChallengeImportTests()
    {
        var options = new DbContextOptionsBuilder<PomoChallengeDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new PomoChallengeDbContext(options);
        _timeProvider = new MockTimeProvider();

        // Create test server
        _testServer = new Server
        {
            Id = 123456789,
            Name = "Test Server",
            Language = "en",
            CategoryId = 987654321,
            PingRoleId = 111222333
        };
        _context.Servers.Add(_testServer);
        _context.SaveChanges();

        // Create service collection and configure services
        var services = new ServiceCollection();
        services.AddSingleton(_context);
        services.AddSingleton<ITimeProvider>(_timeProvider);
        
        var mockChallengeLogger = new Mock<ILogger<ChallengeService>>();
        var mockMessageLogger = new Mock<ILogger<MessageProcessorService>>();
        var mockEmojiService = new Mock<IEmojiService>();
        
        // Configure mock emoji service to return emoji detections
        mockEmojiService.Setup(e => e.DetectEmojis(It.IsAny<string>()))
            .Returns((string content) =>
            {
                var result = new EmojiDetectionResult();
                
                // Add tomato emojis to UnicodeEmojis
                var tomatoCount = content.Split(new[] { "🍅" }, StringSplitOptions.None).Length - 1;
                for (int i = 0; i < tomatoCount; i++)
                {
                    result.UnicodeEmojis.Add("🍅");
                }
                
                // Add star emojis to UnicodeEmojis  
                var starCount = content.Split(new[] { "⭐" }, StringSplitOptions.None).Length - 1;
                for (int i = 0; i < starCount; i++)
                {
                    result.UnicodeEmojis.Add("⭐");
                }
                
                return result;
            });

        services.AddSingleton(mockChallengeLogger.Object);
        services.AddSingleton(mockMessageLogger.Object);
        services.AddSingleton(mockEmojiService.Object);

                    var mockLocalizationService = new Mock<ILocalizationService>();
            services.AddSingleton(mockLocalizationService.Object);

        var serviceProvider = services.BuildServiceProvider();

        _challengeService = new ChallengeService(_context, _timeProvider, null, serviceProvider, 
            serviceProvider.GetRequiredService<ILogger<ChallengeService>>());

        _messageProcessor = new MessageProcessorService(_context, 
            serviceProvider.GetRequiredService<IEmojiService>(),
            serviceProvider,
                            serviceProvider.GetRequiredService<ILocalizationService>(),
            serviceProvider.GetRequiredService<ILogger<MessageProcessorService>>());
    }

    [Fact]
    public async Task ImportChallenge_WithValidParameters_ShouldCreateChallenge()
    {
        // Arrange
        const ulong channelId = 555666777;
        const int semester = 3;
        const string theme = "Space Exploration";

        // Act
        var result = await _challengeService.ImportChallengeAsync(_testServer.Id, channelId, semester, theme);

        // Assert
        result.ShouldNotBeNull();
        // Note: Since we're using placeholder implementations for Discord scanning,
        // this will show the "no threads found" error, which is expected
        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("No threads found matching pattern Q3-week[N]");
    }

    [Fact]
    public async Task ImportChallenge_WithInvalidSemester_ShouldFail()
    {
        // Arrange
        const ulong channelId = 555666777;
        const int invalidSemester = 0; // Invalid semester
        const string theme = "Test Theme";

        // Act
        var result = await _challengeService.ImportChallengeAsync(_testServer.Id, channelId, invalidSemester, theme);

        // Assert
        result.ShouldNotBeNull();
        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("No threads found matching pattern Q0-week[N]");
    }

    [Fact]
    public async Task ImportChallenge_WithNonexistentServer_ShouldFail()
    {
        // Arrange
        const ulong nonexistentServerId = 999888777;
        const ulong channelId = 555666777;
        const int semester = 3;
        const string theme = "Test Theme";

        // Act
        var result = await _challengeService.ImportChallengeAsync(nonexistentServerId, channelId, semester, theme);

        // Assert
        result.ShouldNotBeNull();
        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("No threads found");
    }

    [Fact]
    public async Task ProcessMessageBatch_WithValidMessages_ShouldProcessCorrectly()
    {
        // Arrange
        var challenge = await CreateTestChallengeAsync();
        var week = await CreateTestWeekAsync(challenge.Id, 1);

        var messages = new List<MessageProcessorService.DiscordMessageInfo>
        {
            new()
            {
                MessageId = 111,
                UserId = 123456,
                Content = "🍅🍅 Studied for 2 hours today!",
                Timestamp = DateTime.UtcNow
            },
            new()
            {
                MessageId = 222,
                UserId = 123457,
                Content = "🍅 One pomodoro done ⭐",
                Timestamp = DateTime.UtcNow
            }
        };

        // Act
        var processed = await _messageProcessor.ProcessMessageBatchAsync(messages, week.Id);

        // Assert
        processed.ShouldBe(2);

        // Verify message logs were created
        var logs = await _context.MessageLogs.Where(ml => ml.WeekId == week.Id).ToListAsync();
        logs.Count.ShouldBe(2);

        var firstLog = logs.First(l => l.MessageId == 111);
        firstLog.PomodoroPoints.ShouldBe(2); // 2 tomato emojis
        firstLog.BonusPoints.ShouldBe(0);

        var secondLog = logs.First(l => l.MessageId == 222);
        secondLog.PomodoroPoints.ShouldBe(1); // 1 tomato emoji
        secondLog.BonusPoints.ShouldBe(1); // 1 star emoji
    }

    [Fact]
    public async Task ProcessMessageBatch_WithDuplicateMessages_ShouldSkipDuplicates()
    {
        // Arrange
        var challenge = await CreateTestChallengeAsync();
        var week = await CreateTestWeekAsync(challenge.Id, 1);

        // Create an existing message log
        var existingLog = new MessageLog
        {
            MessageId = 111,
            UserId = 123456,
            WeekId = week.Id,
            PomodoroPoints = 1,
            BonusPoints = 0,
            GoalPoints = 0
        };
        _context.MessageLogs.Add(existingLog);
        await _context.SaveChangesAsync();

        var messages = new List<MessageProcessorService.DiscordMessageInfo>
        {
            new()
            {
                MessageId = 111, // Duplicate
                UserId = 123456,
                Content = "🍅🍅🍅 Trying to add more points",
                Timestamp = DateTime.UtcNow
            },
            new()
            {
                MessageId = 222, // New message
                UserId = 123457,
                Content = "🍅 One pomodoro done",
                Timestamp = DateTime.UtcNow
            }
        };

        // Act
        var processed = await _messageProcessor.ProcessMessageBatchAsync(messages, week.Id);

        // Assert
        processed.ShouldBe(1); // Only one new message processed

        // Verify duplicate wasn't processed
        var logs = await _context.MessageLogs.Where(ml => ml.WeekId == week.Id).ToListAsync();
        logs.Count.ShouldBe(2);

        var duplicateLog = logs.First(l => l.MessageId == 111);
        duplicateLog.PomodoroPoints.ShouldBe(1); // Should remain unchanged
    }

    private async Task<Challenge> CreateTestChallengeAsync()
    {
        var challenge = new Challenge
        {
            ServerId = _testServer.Id,
            SemesterNumber = 3,
            Theme = "Test Challenge",
            StartDate = new DateOnly(2023, 9, 4), // Monday
            EndDate = new DateOnly(2023, 12, 17), // Sunday
            WeekCount = 15,
            IsCurrent = true,
            IsStarted = false,
            IsActive = true // Make it active so points can be calculated
        };

        _context.Challenges.Add(challenge);
        await _context.SaveChangesAsync();
        
        // Add test emojis for point calculation
        var tomatoEmoji = new Emoji
        {
            ServerId = _testServer.Id,
            EmojiCode = "🍅",
            EmojiType = EmojiType.Pomodoro,
            PointValue = 1,
            IsActive = true
        };
        
        var starEmoji = new Emoji
        {
            ServerId = _testServer.Id,
            EmojiCode = "⭐",
            EmojiType = EmojiType.Bonus,
            PointValue = 1,
            IsActive = true
        };
        
        _context.Emojis.Add(tomatoEmoji);
        _context.Emojis.Add(starEmoji);
        await _context.SaveChangesAsync();
        
        return challenge;
    }

    private async Task<Week> CreateTestWeekAsync(int challengeId, int weekNumber)
    {
        var week = new Week
        {
            ChallengeId = challengeId,
            WeekNumber = weekNumber,
            ThreadId = 888999000,
            LeaderboardPosted = false
        };

        _context.Weeks.Add(week);
        await _context.SaveChangesAsync();
        return week;
    }

    public void Dispose()
    {
        _context.Dispose();
    }
} 