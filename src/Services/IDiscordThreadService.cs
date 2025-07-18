using PomoChallengeCounter.Models.Results;

namespace PomoChallengeCounter.Services;

/// <summary>
/// Interface for Discord thread operations - abstracted for testing
/// </summary>
public interface IDiscordThreadService
{
    /// <summary>
    /// Create a new thread in a Discord channel
    /// </summary>
    Task<DiscordThreadResult> CreateThreadAsync(ulong serverId, ulong categoryId, string threadName, int weekNumber, string? welcomeMessage = null, ulong? pingRoleId = null);
    
    /// <summary>
    /// Create a new thread for a specific challenge, creating the challenge channel if needed
    /// </summary>
    Task<DiscordThreadResult> CreateChallengeThreadAsync(ulong serverId, ulong categoryId, string threadName, int weekNumber, string challengeTheme, int semesterNumber, string? welcomeMessage = null, ulong? pingRoleId = null);
    
    /// <summary>
    /// Find a suitable channel for thread creation in a category
    /// </summary>
    Task<DiscordChannelResult> FindChannelForChallengeAsync(ulong serverId, ulong categoryId, string challengeTheme, int semesterNumber);
    
    /// <summary>
    /// Send a message to a thread
    /// </summary>
    Task<bool> SendMessageToThreadAsync(ulong threadId, string content, ulong? pingRoleId = null);
    
    /// <summary>
    /// Get thread information by ID
    /// </summary>
    Task<DiscordThreadInfo?> GetThreadInfoAsync(ulong threadId);
    
    /// <summary>
    /// Check if a thread exists and is accessible
    /// </summary>
    Task<bool> ThreadExistsAsync(ulong threadId);
    
    /// <summary>
    /// Create a dedicated channel for a challenge within a category
    /// </summary>
    Task<DiscordChannelResult> CreateChallengeChannelAsync(ulong serverId, ulong categoryId, string challengeTheme, int semesterNumber);
} 