using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PomoChallengeCounter.Data;
using PomoChallengeCounter.Models;
using PomoChallengeCounter.Models.Results;

namespace PomoChallengeCounter.Services;

public class ChallengeService(
    PomoChallengeDbContext context,
    ITimeProvider timeProvider,
    ILogger<ChallengeService> logger) : IChallengeService
{
    public async Task<ChallengeOperationResult> CreateChallengeAsync(ulong serverId, int semesterNumber, string theme, DateOnly startDate, DateOnly endDate, int weekCount)
    {
        try
        {
            // Validate parameters first
            var validation = ValidateChallengeParameters(semesterNumber, theme, startDate, endDate, weekCount);
            if (!validation.IsValid)
            {
                logger.LogWarning("Challenge creation failed validation for server {ServerId}: {Errors}",
                    serverId, string.Join(", ", validation.Errors));
                return ChallengeOperationResult.Failure($"Validation failed: {string.Join(", ", validation.Errors)}");
            }

            // Check if server exists
            var serverExists = await context.Servers.AnyAsync(s => s.Id == serverId);
            if (!serverExists)
            {
                logger.LogWarning("Attempted to create challenge for non-existent server {ServerId}", serverId);
                return ChallengeOperationResult.Failure("Server not found");
            }

            // Check for conflicting challenges (same semester for same server)
            var existingChallenge = await context.Challenges
                .FirstOrDefaultAsync(c => c.ServerId == serverId && c.SemesterNumber == semesterNumber);
            
            if (existingChallenge != null)
            {
                logger.LogWarning("Challenge creation failed: Semester {Semester} already exists for server {ServerId}",
                    semesterNumber, serverId);
                return ChallengeOperationResult.Failure($"Challenge for semester {semesterNumber} already exists");
            }

            // Create the challenge
            var challenge = new Challenge
            {
                ServerId = serverId,
                SemesterNumber = semesterNumber,
                Theme = theme.Trim(),
                StartDate = startDate,
                EndDate = endDate,
                WeekCount = weekCount,
                IsStarted = false,
                IsActive = false,
                IsCurrent = false
            };

            context.Challenges.Add(challenge);
            await context.SaveChangesAsync();

            logger.LogInformation("Created challenge {ChallengeId} for server {ServerId}: S{Semester} '{Theme}'",
                challenge.Id, serverId, semesterNumber, theme);

            return ChallengeOperationResult.Success(challenge);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create challenge for server {ServerId}", serverId);
            return ChallengeOperationResult.Failure("Internal error occurred");
        }
    }

    public async Task<ChallengeOperationResult> StartChallengeAsync(int challengeId)
    {
        try
        {
            var challenge = await context.Challenges
                .Include(c => c.Server)
                .FirstOrDefaultAsync(c => c.Id == challengeId);

            if (challenge == null)
            {
                return ChallengeOperationResult.Failure("Challenge not found");
            }

            if (challenge.IsStarted)
            {
                return ChallengeOperationResult.Failure("Challenge is already started");
            }

            // Deactivate any other current challenges for this server
            var otherCurrentChallenges = await context.Challenges
                .Where(c => c.ServerId == challenge.ServerId && c.IsCurrent && c.Id != challengeId)
                .ToListAsync();

            foreach (var otherChallenge in otherCurrentChallenges)
            {
                otherChallenge.IsCurrent = false;
                otherChallenge.IsActive = false;
                logger.LogInformation("Deactivated previous challenge {ChallengeId} for server {ServerId}",
                    otherChallenge.Id, challenge.ServerId);
            }

            // Start the challenge
            challenge.IsStarted = true;
            challenge.IsActive = true;
            challenge.IsCurrent = true;

            // Create week 0 (goal setting week) and week 1
            await CreateInitialWeeksAsync(challenge);

            await context.SaveChangesAsync();

            logger.LogInformation("Started challenge {ChallengeId} for server {ServerId}: S{Semester} '{Theme}'",
                challengeId, challenge.ServerId, challenge.SemesterNumber, challenge.Theme);

            return ChallengeOperationResult.Success(challenge);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start challenge {ChallengeId}", challengeId);
            return ChallengeOperationResult.Failure("Internal error occurred");
        }
    }

    public async Task<ChallengeOperationResult> StopChallengeAsync(int challengeId)
    {
        try
        {
            var challenge = await context.Challenges.FirstOrDefaultAsync(c => c.Id == challengeId);

            if (challenge == null)
            {
                return ChallengeOperationResult.Failure("Challenge not found");
            }

            if (!challenge.IsStarted)
            {
                return ChallengeOperationResult.Failure("Challenge is not started");
            }

            // Stop the challenge
            challenge.IsActive = false;
            challenge.IsCurrent = false;

            await context.SaveChangesAsync();

            logger.LogInformation("Stopped challenge {ChallengeId} for server {ServerId}",
                challengeId, challenge.ServerId);

            return ChallengeOperationResult.Success(challenge);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to stop challenge {ChallengeId}", challengeId);
            return ChallengeOperationResult.Failure("Internal error occurred");
        }
    }

    public async Task<ChallengeOperationResult> DeactivateChallengeAsync(int challengeId)
    {
        try
        {
            var challenge = await context.Challenges.FirstOrDefaultAsync(c => c.Id == challengeId);

            if (challenge == null)
            {
                return ChallengeOperationResult.Failure("Challenge not found");
            }

            // Deactivate (stops message processing but keeps Discord content)
            challenge.IsActive = false;

            await context.SaveChangesAsync();

            logger.LogInformation("Deactivated challenge {ChallengeId} for server {ServerId} (Discord content preserved)",
                challengeId, challenge.ServerId);

            return ChallengeOperationResult.Success(challenge);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deactivate challenge {ChallengeId}", challengeId);
            return ChallengeOperationResult.Failure("Internal error occurred");
        }
    }

    public async Task<Challenge?> GetCurrentChallengeAsync(ulong serverId)
    {
        return await context.Challenges
            .Include(c => c.Server)
            .Include(c => c.Weeks)
            .FirstOrDefaultAsync(c => c.ServerId == serverId && c.IsCurrent);
    }

    public async Task<Challenge?> GetChallengeAsync(int challengeId)
    {
        return await context.Challenges
            .Include(c => c.Server)
            .Include(c => c.Weeks)
            .Include(c => c.Emojis)
            .FirstOrDefaultAsync(c => c.Id == challengeId);
    }

    public async Task<List<Challenge>> GetServerChallengesAsync(ulong serverId)
    {
        return await context.Challenges
            .Where(c => c.ServerId == serverId)
            .OrderByDescending(c => c.SemesterNumber)
            .ThenByDescending(c => c.Id)
            .ToListAsync();
    }

    public ChallengeValidationResult ValidateChallengeParameters(int semesterNumber, string theme, DateOnly startDate, DateOnly endDate, int weekCount)
    {
        var result = new ChallengeValidationResult { IsValid = true };

        // Validate semester number
        if (semesterNumber < 1 || semesterNumber > 5)
        {
            result.AddError("Semester number must be between 1-5 (1-4: regular semesters, 5: summer)");
        }

        // Validate theme
        if (string.IsNullOrWhiteSpace(theme))
        {
            result.AddError("Theme is required");
        }
        else if (theme.Trim().Length > 255)
        {
            result.AddError("Theme must be 255 characters or less");
        }

        // Validate start date (must be Monday)
        if (startDate.DayOfWeek != DayOfWeek.Monday)
        {
            result.AddError("Start date must be a Monday");
        }

        // Validate end date (must be Sunday)
        if (endDate.DayOfWeek != DayOfWeek.Sunday)
        {
            result.AddError("End date must be a Sunday");
        }

        // Validate date range
        if (endDate <= startDate)
        {
            result.AddError("End date must be after start date");
        }

        // Validate week count matches date range
        if (startDate.DayOfWeek == DayOfWeek.Monday && endDate.DayOfWeek == DayOfWeek.Sunday)
        {
            var daysDifference = endDate.DayNumber - startDate.DayNumber + 1;
            var expectedWeeks = daysDifference / 7;
            
            if (weekCount != expectedWeeks)
            {
                result.AddError($"Week count ({weekCount}) does not match date range ({expectedWeeks} weeks from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd})");
            }
        }

        // Validate minimum duration
        if (weekCount < 1)
        {
            result.AddError("Challenge must be at least 1 week long");
        }

        // Validate maximum duration (reasonable limit)
        if (weekCount > 52)
        {
            result.AddError("Challenge cannot be longer than 52 weeks");
        }

        // Validate dates are not in the past (with some tolerance for today)
        var today = timeProvider.Today;
        if (startDate < today)
        {
            result.AddError("Start date cannot be in the past");
        }

        return result;
    }

    private async Task CreateInitialWeeksAsync(Challenge challenge)
    {
        // Create week 0 (goal setting week)
        var week0 = new Week
        {
            ChallengeId = challenge.Id,
            WeekNumber = 0,
            ThreadId = 0, // Will be set when Discord thread is created
            LeaderboardPosted = false
        };
        context.Weeks.Add(week0);

        // Create week 1 (first active week)
        var week1 = new Week
        {
            ChallengeId = challenge.Id,
            WeekNumber = 1,
            ThreadId = 0, // Will be set when Discord thread is created
            LeaderboardPosted = false
        };
        context.Weeks.Add(week1);

        logger.LogDebug("Created initial weeks 0 and 1 for challenge {ChallengeId}", challenge.Id);
    }
} 