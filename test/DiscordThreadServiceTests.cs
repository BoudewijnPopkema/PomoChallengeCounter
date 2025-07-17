using Shouldly;

namespace PomoChallengeCounter.Tests;

public class DiscordThreadServiceTests : IDisposable
{
    private readonly MockDiscordThreadService _mockDiscordThreadService;

    public DiscordThreadServiceTests()
    {
        _mockDiscordThreadService = new MockDiscordThreadService();
    }

    [Fact]
    public async Task CreateThreadAsync_WithValidParameters_ShouldCreateThread()
    {
        // Arrange
        const ulong serverId = 12345;
        const ulong categoryId = 67890;
        const string threadName = "Q3-week1";
        const int weekNumber = 1;
        const string welcomeMessage = "Welcome to week 1!";
        const ulong pingRoleId = 11111;

        // Act
        var result = await _mockDiscordThreadService.CreateThreadAsync(serverId, categoryId, threadName, weekNumber, welcomeMessage, pingRoleId);

        // Assert
        result.ShouldNotBeNull();
        result.IsSuccess.ShouldBeTrue();
        result.ThreadId.ShouldBeGreaterThan(0ul);
        result.ThreadName.ShouldBe(threadName);
        result.MessageSent.ShouldBeTrue();
        _mockDiscordThreadService.CreatedThreadCount.ShouldBe(1);
    }

    [Fact]
    public async Task CreateThreadAsync_WhenConfiguredToFail_ShouldReturnFailure()
    {
        // Arrange
        const ulong serverId = 12345;
        const ulong categoryId = 67890;
        const string threadName = "Q3-week1";
        const int weekNumber = 1;

        _mockDiscordThreadService.ShouldFailThreadCreation = true;
        _mockDiscordThreadService.FailureMessage = "Mock thread creation failure";

        // Act
        var result = await _mockDiscordThreadService.CreateThreadAsync(serverId, categoryId, threadName, weekNumber);

        // Assert
        result.ShouldNotBeNull();
        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldBe("Mock thread creation failure");
        _mockDiscordThreadService.CreatedThreadCount.ShouldBe(0);
    }

    [Fact]
    public async Task CreateThreadAsync_WithoutWelcomeMessage_ShouldNotSendMessage()
    {
        // Arrange
        const ulong serverId = 12345;
        const ulong categoryId = 67890;
        const string threadName = "Q3-week1";
        const int weekNumber = 1;

        // Act
        var result = await _mockDiscordThreadService.CreateThreadAsync(serverId, categoryId, threadName, weekNumber);

        // Assert
        result.ShouldNotBeNull();
        result.IsSuccess.ShouldBeTrue();
        result.MessageSent.ShouldBeFalse();
    }

    [Fact]
    public async Task FindChannelForChallengeAsync_WithValidParameters_ShouldReturnChannel()
    {
        // Arrange
        const ulong serverId = 12345;
        const ulong categoryId = 67890;
        const string challengeTheme = "Space Exploration";
        const int semesterNumber = 3;

        // Act
        var result = await _mockDiscordThreadService.FindChannelForChallengeAsync(serverId, categoryId, challengeTheme, semesterNumber);

        // Assert
        result.ShouldNotBeNull();
        result.IsSuccess.ShouldBeTrue();
        result.ChannelId.ShouldBeGreaterThan(0ul);
        result.ChannelName.ShouldBe("space-exploration");
    }

    [Fact]
    public async Task FindChannelForChallengeAsync_WhenConfiguredToFail_ShouldReturnFailure()
    {
        // Arrange
        const ulong serverId = 12345;
        const ulong categoryId = 67890;
        const string challengeTheme = "Test";
        const int semesterNumber = 3;

        _mockDiscordThreadService.ShouldFailChannelSearch = true;
        _mockDiscordThreadService.FailureMessage = "Channel search failed";

        // Act
        var result = await _mockDiscordThreadService.FindChannelForChallengeAsync(serverId, categoryId, challengeTheme, semesterNumber);

        // Assert
        result.ShouldNotBeNull();
        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldBe("Channel search failed");
    }

    [Fact]
    public async Task SendMessageToThreadAsync_WithValidThread_ShouldReturnTrue()
    {
        // Arrange
        const string content = "Test message";
        const ulong pingRoleId = 11111;

        // First create a thread
        var createResult = await _mockDiscordThreadService.CreateThreadAsync(12345, 67890, "test-thread", 1);
        var threadId = createResult.ThreadId;

        // Act
        var result = await _mockDiscordThreadService.SendMessageToThreadAsync(threadId, content, pingRoleId);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task SendMessageToThreadAsync_WithInvalidThread_ShouldReturnFalse()
    {
        // Arrange
        const ulong invalidThreadId = 99999;
        const string content = "Test message";

        // Act
        var result = await _mockDiscordThreadService.SendMessageToThreadAsync(invalidThreadId, content);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task GetThreadInfoAsync_WithValidThread_ShouldReturnThreadInfo()
    {
        // Arrange
        const string threadName = "Q3-week1";

        // First create a thread
        var createResult = await _mockDiscordThreadService.CreateThreadAsync(12345, 67890, threadName, 1);
        var threadId = createResult.ThreadId;

        // Act
        var result = await _mockDiscordThreadService.GetThreadInfoAsync(threadId);

        // Assert
        result.ShouldNotBeNull();
        result.ThreadId.ShouldBe(threadId);
        result.Name.ShouldBe(threadName);
        result.IsArchived.ShouldBeFalse();
        result.IsLocked.ShouldBeFalse();
    }

    [Fact]
    public async Task GetThreadInfoAsync_WithInvalidThread_ShouldReturnNull()
    {
        // Arrange
        const ulong invalidThreadId = 99999;

        // Act
        var result = await _mockDiscordThreadService.GetThreadInfoAsync(invalidThreadId);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task ThreadExistsAsync_WithValidThread_ShouldReturnTrue()
    {
        // Arrange
        // First create a thread
        var createResult = await _mockDiscordThreadService.CreateThreadAsync(12345, 67890, "test-thread", 1);
        var threadId = createResult.ThreadId;

        // Act
        var result = await _mockDiscordThreadService.ThreadExistsAsync(threadId);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task ThreadExistsAsync_WithInvalidThread_ShouldReturnFalse()
    {
        // Arrange
        const ulong invalidThreadId = 99999;

        // Act
        var result = await _mockDiscordThreadService.ThreadExistsAsync(invalidThreadId);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task MockService_Reset_ShouldClearAllData()
    {
        // Arrange
        await _mockDiscordThreadService.CreateThreadAsync(12345, 67890, "test-thread1", 1);
        await _mockDiscordThreadService.CreateThreadAsync(12345, 67890, "test-thread2", 2);
        
        _mockDiscordThreadService.CreatedThreadCount.ShouldBe(2);

        // Act
        _mockDiscordThreadService.Reset();

        // Assert
        _mockDiscordThreadService.CreatedThreadCount.ShouldBe(0);
        _mockDiscordThreadService.ShouldFailThreadCreation.ShouldBeFalse();
        _mockDiscordThreadService.ShouldFailChannelSearch.ShouldBeFalse();
        _mockDiscordThreadService.ShouldFailMessageSending.ShouldBeFalse();
    }

    [Fact]
    public async Task MockService_MultipleThreadCreation_ShouldTrackAllThreads()
    {
        // Arrange & Act
        var result1 = await _mockDiscordThreadService.CreateThreadAsync(12345, 67890, "thread1", 1);
        var result2 = await _mockDiscordThreadService.CreateThreadAsync(12345, 67890, "thread2", 2);
        var result3 = await _mockDiscordThreadService.CreateThreadAsync(12345, 67890, "thread3", 3);

        // Assert
        _mockDiscordThreadService.CreatedThreadCount.ShouldBe(3);
        _mockDiscordThreadService.WasThreadCreated(result1.ThreadId).ShouldBeTrue();
        _mockDiscordThreadService.WasThreadCreated(result2.ThreadId).ShouldBeTrue();
        _mockDiscordThreadService.WasThreadCreated(result3.ThreadId).ShouldBeTrue();

        var createdThreadIds = _mockDiscordThreadService.GetCreatedThreadIds();
        createdThreadIds.Count.ShouldBe(3);
        createdThreadIds.ShouldContain(result1.ThreadId);
        createdThreadIds.ShouldContain(result2.ThreadId);
        createdThreadIds.ShouldContain(result3.ThreadId);
    }

    [Fact]
    public async Task CreateChallengeChannelAsync_WithValidParameters_ShouldCreateChannel()
    {
        // Arrange
        const ulong serverId = 12345;
        const ulong categoryId = 67890;
        const string challengeTheme = "Space Exploration";
        const int semesterNumber = 3;

        // Act
        var result = await _mockDiscordThreadService.CreateChallengeChannelAsync(serverId, categoryId, challengeTheme, semesterNumber);

        // Assert
        result.ShouldNotBeNull();
        result.IsSuccess.ShouldBeTrue();
        result.ChannelId.ShouldBeGreaterThan(0ul);
        result.ChannelName.ShouldBe("q3-space-exploration-challenge");
    }

    [Fact]
    public async Task CreateChallengeChannelAsync_WhenConfiguredToFail_ShouldReturnFailure()
    {
        // Arrange
        const ulong serverId = 12345;
        const ulong categoryId = 67890;
        const string challengeTheme = "Test Challenge";
        const int semesterNumber = 5;

        _mockDiscordThreadService.ShouldFailChannelSearch = true;
        _mockDiscordThreadService.FailureMessage = "Channel creation failed";

        // Act
        var result = await _mockDiscordThreadService.CreateChallengeChannelAsync(serverId, categoryId, challengeTheme, semesterNumber);

        // Assert
        result.ShouldNotBeNull();
        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldBe("Channel creation failed");
    }

    public void Dispose()
    {
        _mockDiscordThreadService?.Reset();
    }
} 