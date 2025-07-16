using PomoChallengeCounter.Models;
using PomoChallengeCounter.Models.Results;

namespace PomoChallengeCounter.Services;

/// <summary>
/// Service for managing challenge lifecycle and operations
/// </summary>
public interface IChallengeService
{
    /// <summary>
    /// Create a new challenge for a server
    /// </summary>
    Task<ChallengeOperationResult> CreateChallengeAsync(ulong serverId, int semesterNumber, string theme, DateOnly startDate, DateOnly endDate, int weekCount);
    
    /// <summary>
    /// Start an existing challenge (creates initial threads and activates tracking)
    /// </summary>
    Task<ChallengeOperationResult> StartChallengeAsync(int challengeId);
    
    /// <summary>
    /// Stop/complete a running challenge
    /// </summary>
    Task<ChallengeOperationResult> StopChallengeAsync(int challengeId);
    
    /// <summary>
    /// Deactivate a challenge without deleting Discord content (stops message processing)
    /// </summary>
    Task<ChallengeOperationResult> DeactivateChallengeAsync(int challengeId);
    
    /// <summary>
    /// Get current active challenge for a server
    /// </summary>
    Task<Challenge?> GetCurrentChallengeAsync(ulong serverId);
    
    /// <summary>
    /// Get challenge by ID
    /// </summary>
    Task<Challenge?> GetChallengeAsync(int challengeId);
    
    /// <summary>
    /// List all challenges for a server
    /// </summary>
    Task<List<Challenge>> GetServerChallengesAsync(ulong serverId);
    
    /// <summary>
    /// Validate challenge parameters before creation
    /// </summary>
    ChallengeValidationResult ValidateChallengeParameters(int semesterNumber, string theme, DateOnly startDate, DateOnly endDate, int weekCount);
} 