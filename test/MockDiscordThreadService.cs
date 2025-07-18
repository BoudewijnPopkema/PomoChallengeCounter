using PomoChallengeCounter.Models.Results;
using PomoChallengeCounter.Services;

namespace PomoChallengeCounter.Tests;

/// <summary>
/// Mock implementation of IDiscordThreadService for testing
/// </summary>
public class MockDiscordThreadService : IDiscordThreadService
{
    private static ulong _nextThreadId = 100000;
    private readonly Dictionary<ulong, DiscordThreadInfo> _threads = new();
    private readonly HashSet<ulong> _createdThreads = new();

    public bool ShouldFailThreadCreation { get; set; } = false;
    public bool ShouldFailChannelSearch { get; set; } = false;
    public bool ShouldFailMessageSending { get; set; } = false;
    public string? FailureMessage { get; set; } = "Mock failure";

    public Task<DiscordThreadResult> CreateThreadAsync(ulong serverId, ulong categoryId, string threadName, int weekNumber, string? welcomeMessage = null, ulong? pingRoleId = null)
    {
        if (ShouldFailThreadCreation)
        {
            return Task.FromResult(DiscordThreadResult.Failure(FailureMessage ?? "Thread creation failed"));
        }

        var threadId = _nextThreadId++;
        var messageSent = !ShouldFailMessageSending && !string.IsNullOrEmpty(welcomeMessage);

        var threadInfo = new DiscordThreadInfo
        {
            ThreadId = threadId,
            Name = threadName,
            ChannelId = categoryId, // Using categoryId as parent for simplicity
            IsArchived = false,
            IsLocked = false,
            CreatedAt = DateTime.UtcNow,
            MessageCount = 0 // New threads start with 0 messages
        };

        _threads[threadId] = threadInfo;
        _createdThreads.Add(threadId);

        return Task.FromResult(DiscordThreadResult.Success(threadId, threadName, messageSent));
    }

    public Task<DiscordThreadResult> CreateChallengeThreadAsync(ulong serverId, ulong categoryId, string threadName, int weekNumber, string challengeTheme, int semesterNumber, string? welcomeMessage = null, ulong? pingRoleId = null)
    {
        // For testing, just delegate to the regular CreateThreadAsync method
        return CreateThreadAsync(serverId, categoryId, threadName, weekNumber, welcomeMessage, pingRoleId);
    }

    public Task<DiscordChannelResult> FindChannelForChallengeAsync(ulong serverId, ulong categoryId, string challengeTheme, int semesterNumber)
    {
        if (ShouldFailChannelSearch)
        {
            return Task.FromResult(DiscordChannelResult.Failure(FailureMessage ?? "Channel search failed"));
        }

        // Mock successful channel finding
        var channelId = categoryId + 1000; // Simple mock channel ID
        var channelName = challengeTheme.ToLowerInvariant().Replace(" ", "-");

        return Task.FromResult(DiscordChannelResult.Success(channelId, channelName));
    }

    public Task<bool> SendMessageToThreadAsync(ulong threadId, string content, ulong? pingRoleId = null)
    {
        if (ShouldFailMessageSending)
        {
            return Task.FromResult(false);
        }

        // Return true if thread exists
        return Task.FromResult(_threads.ContainsKey(threadId));
    }

    public Task<DiscordThreadInfo?> GetThreadInfoAsync(ulong threadId)
    {
        _threads.TryGetValue(threadId, out var threadInfo);
        return Task.FromResult(threadInfo);
    }

    public Task<bool> ThreadExistsAsync(ulong threadId)
    {
        return Task.FromResult(_threads.ContainsKey(threadId));
    }

    // Test helper methods
    public void Reset()
    {
        _threads.Clear();
        _createdThreads.Clear();
        ShouldFailThreadCreation = false;
        ShouldFailChannelSearch = false;
        ShouldFailMessageSending = false;
        FailureMessage = "Mock failure";
    }

    public IReadOnlyCollection<ulong> GetCreatedThreadIds()
    {
        return _createdThreads.ToList().AsReadOnly();
    }

    public int CreatedThreadCount => _createdThreads.Count;

    public bool WasThreadCreated(ulong threadId)
    {
        return _createdThreads.Contains(threadId);
    }

    public Task<DiscordChannelResult> CreateChallengeChannelAsync(ulong serverId, ulong categoryId, string challengeTheme, int semesterNumber)
    {
        if (ShouldFailChannelSearch)
        {
            return Task.FromResult(DiscordChannelResult.Failure(FailureMessage ?? "Channel creation failed"));
        }

        // Mock successful channel creation
        var channelName = $"q{semesterNumber}-{challengeTheme.ToLowerInvariant().Replace(" ", "-")}-challenge";
        var channelId = categoryId + 2000; // Different from FindChannelForChallengeAsync to distinguish

        return Task.FromResult(DiscordChannelResult.Success(channelId, channelName));
    }
} 