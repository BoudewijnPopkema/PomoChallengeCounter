using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Gateway;
using PomoChallengeCounter.Data;
using PomoChallengeCounter.Models;
using PomoChallengeCounter.Models.Results;

namespace PomoChallengeCounter.Services;

public class ChallengeService(
    PomoChallengeDbContext context,
    ITimeProvider timeProvider,
    GatewayClient gatewayClient,
    IServiceProvider serviceProvider,
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

    public async Task<ChallengeImportResult> ImportChallengeAsync(ulong serverId, ulong channelId, int semesterNumber, string theme)
    {
        var result = new ChallengeImportResult();

        try
        {
            logger.LogInformation("Starting challenge import for server {ServerId}, channel {ChannelId}, semester {Semester}, theme {Theme}", 
                serverId, channelId, semesterNumber, theme);

            // 1. Scan Discord channel for threads matching Q[semester]-week[N] pattern
            var threadScanResult = await ScanChannelForChallengeThreadsAsync(channelId, semesterNumber);
            
            if (!threadScanResult.Any())
            {
                result.ErrorMessage = $"No threads found matching pattern Q{semesterNumber}-week[N] in the specified channel";
                return result;
            }

            result.ThreadsFound = threadScanResult.Select(t => t.Name).ToList();
            result.ThreadsProcessed = threadScanResult.Count;

            // 2. Determine challenge date range from threads
            var dateRange = CalculateDateRangeFromThreads(threadScanResult);
            
            if (dateRange.startDate == null || dateRange.endDate == null)
            {
                result.ErrorMessage = "Unable to determine valid date range from found threads";
                return result;
            }

            var weekCount = threadScanResult.Max(t => t.WeekNumber);

            // 3. Validate challenge parameters
            var validation = ValidateChallengeParameters(semesterNumber, theme, dateRange.startDate.Value, dateRange.endDate.Value, weekCount);
            if (!validation.IsValid)
            {
                result.ErrorMessage = $"Challenge validation failed: {string.Join(", ", validation.Errors)}";
                return result;
            }

            // 4. Create challenge record
            var challenge = new Challenge
            {
                ServerId = serverId,
                SemesterNumber = semesterNumber,
                Theme = theme,
                StartDate = dateRange.startDate.Value,
                EndDate = dateRange.endDate.Value,
                WeekCount = weekCount,
                IsCurrent = true,
                IsStarted = false,  // Imported challenges start as inactive
                IsActive = false   // Admin needs to explicitly start them
            };

            context.Challenges.Add(challenge);
            await context.SaveChangesAsync();

            logger.LogInformation("Created challenge {ChallengeId} from import", challenge.Id);

            // 5. Create week records for all found threads
            await CreateWeekRecordsFromThreadsAsync(challenge.Id, threadScanResult);

                        // 6. Process historical messages from all threads
            var messageProcessor = serviceProvider.GetRequiredService<MessageProcessorService>();
            var totalMessagesProcessed = 0;
            var usersFound = new HashSet<ulong>();

            foreach (var threadInfo in threadScanResult)
            {
                try
                {
                    logger.LogInformation("Processing historical messages from thread {ThreadName} ({ThreadId})", 
                        threadInfo.Name, threadInfo.ThreadId);

                    var messagesProcessed = await ProcessHistoricalMessagesFromThreadAsync(
                        messageProcessor, threadInfo.ThreadId, challenge.Id, threadInfo.WeekNumber);
                     
                    totalMessagesProcessed += messagesProcessed.messagesProcessed;
                    foreach (var userId in messagesProcessed.usersFound)
                    {
                        usersFound.Add(userId);
                    }

                    logger.LogInformation("Processed {MessageCount} messages from thread {ThreadName}", 
                        messagesProcessed.messagesProcessed, threadInfo.Name);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to process messages from thread {ThreadName}: {Error}", 
                        threadInfo.Name, ex.Message);
                    result.Warnings.Add($"Failed to process thread {threadInfo.Name}: {ex.Message}");
                }
            }

            result.Challenge = challenge;
            result.MessagesProcessed = totalMessagesProcessed;
            result.UsersFound = usersFound.Count;
            result.IsSuccess = true;

            logger.LogInformation("Challenge import completed successfully. Challenge: {ChallengeId}, Threads: {ThreadCount}, Messages: {MessageCount}, Users: {UserCount}", 
                challenge.Id, result.ThreadsProcessed, result.MessagesProcessed, result.UsersFound);

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Challenge import failed for server {ServerId}, channel {ChannelId}", serverId, channelId);
            result.ErrorMessage = $"Import failed: {ex.Message}";
            return result;
        }
    }

    private async Task<List<ThreadInfo>> ScanChannelForChallengeThreadsAsync(ulong channelId, int semesterNumber)
    {
        var threads = new List<ThreadInfo>();

        try
        {
            logger.LogInformation("Scanning channel {ChannelId} for Q{Semester}-week[N] threads", channelId, semesterNumber);

            // Get the channel
            var channel = await gatewayClient.Rest.GetChannelAsync(channelId);

            // Check if it's a text channel that can have threads
            if (channel is TextGuildChannel textChannel)
            {
                try
                {
                    logger.LogDebug("Scanning threads from text channel {ChannelId}", channelId);
                    
                    // Get the guild to enumerate all channels/threads
                    var guild = await gatewayClient.Rest.GetGuildAsync(textChannel.GuildId);
                    var allChannels = await guild.GetChannelsAsync();
                    
                    // Filter for GuildThread channels that belong to our target channel
                    var guildThreads = allChannels
                        .OfType<GuildThread>()
                        .Where(t => t.ParentId == channelId)
                        .ToList();
                    
                    logger.LogDebug("Found {ThreadCount} threads in channel {ChannelId}", guildThreads.Count, channelId);
                    
                    // Process each thread and check if it matches the challenge pattern
                    foreach (var thread in guildThreads)
                    {
                        var (isMatch, weekNumber) = ParseThreadName(thread.Name, semesterNumber);
                        
                        if (isMatch)
                        {
                            var threadInfo = new ThreadInfo
                            {
                                Name = thread.Name,
                                ThreadId = thread.Id,
                                WeekNumber = weekNumber,
                                CreatedAt = thread.CreatedAt.DateTime
                            };
                            
                            threads.Add(threadInfo);
                            
                            logger.LogDebug("Found matching thread: {ThreadName} (ID: {ThreadId}) for week {WeekNumber}", 
                                thread.Name, thread.Id, weekNumber);
                        }
                        else
                        {
                            logger.LogDebug("Thread {ThreadName} does not match Q{Semester}-week[N] pattern", 
                                thread.Name, semesterNumber);
                        }
                    }
                    
                    logger.LogInformation("Successfully scanned channel {ChannelId}, found {MatchingThreads} matching threads out of {TotalThreads} total threads", 
                        channelId, threads.Count, guildThreads.Count);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to retrieve threads from channel {ChannelId}", channelId);
                }
            }
            else
            {
                logger.LogWarning("Channel {ChannelId} is not a text channel, cannot scan for threads", channelId);
            }

            // Sort threads by week number for consistent processing
            threads.Sort((a, b) => a.WeekNumber.CompareTo(b.WeekNumber));

            logger.LogInformation("Found {ThreadCount} challenge threads in channel {ChannelId} for semester {Semester}", 
                threads.Count, channelId, semesterNumber);

            return threads;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error scanning channel {ChannelId} for threads", channelId);
            return threads;
        }
    }

    // NOTE: NetCord thread scanning implementation complete
    // The GetChannelsAsync() method returns both active and archived threads as GuildThread objects
    // Additional pagination support could be added if needed for large numbers of threads

    // Helper method to parse thread names and extract week numbers
    private static (bool isMatch, int weekNumber) ParseThreadName(string threadName, int semesterNumber)
    {
        if (string.IsNullOrWhiteSpace(threadName))
            return (false, 0);

        // Pattern: Q{semester}-week{number} with optional suffix
        // Examples: Q3-week1, Q1-week0-inzet, q1-week1-inzet, Q5-Week0-Inzet, Q3-WEEK12, q5-week1
        // Case insensitive matching - supports goal threads like "q1-week0-inzet" or "q1-week1-inzet"
        
        var expectedPrefix = $"Q{semesterNumber}-week";
        
        if (threadName.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var afterPrefix = threadName.Substring(expectedPrefix.Length);
            
            // Extract just the number part (before any additional dashes/suffixes)
            var weekPart = afterPrefix.Split('-')[0];
            
            if (int.TryParse(weekPart, out var weekNumber) && weekNumber >= 0)
            {
                return (true, weekNumber);
            }
        }

        return (false, 0);
    }

    // Helper method to validate thread names match expected pattern
    private static bool IsValidChallengeThread(string threadName, int semesterNumber)
    {
        var (isMatch, weekNumber) = ParseThreadName(threadName, semesterNumber);
        return isMatch && weekNumber >= 0; // Week 0 is valid for goal setting
    }

    // Helper method to extract week number from thread name
    private static int GetWeekNumberFromThreadName(string threadName, int semesterNumber)
    {
        var (isMatch, weekNumber) = ParseThreadName(threadName, semesterNumber);
        return isMatch ? weekNumber : -1;
    }

    private (DateOnly? startDate, DateOnly? endDate) CalculateDateRangeFromThreads(List<ThreadInfo> threads)
    {
        if (!threads.Any())
        {
            logger.LogWarning("No threads provided for date calculation");
            return (null, null);
        }

        try
        {
            // Sort threads by week number to find the chronological order
            var sortedThreads = threads.OrderBy(t => t.WeekNumber).ToList();
            
            // Find week 0 or 1 as the start reference
            var startWeekThread = sortedThreads.FirstOrDefault(t => t.WeekNumber == 0) ?? 
                                  sortedThreads.FirstOrDefault(t => t.WeekNumber == 1);
            
            if (startWeekThread == null)
            {
                logger.LogWarning("Could not find week 0 or 1 thread for start date calculation");
                return (null, null);
            }

            // Calculate start date based on the earliest thread
            // Week 0 starts the Sunday before the thread was created
            // Week 1 starts on the Monday the week of thread creation
            DateOnly calculatedStartDate;
            
            if (startWeekThread.WeekNumber == 0)
            {
                // Week 0 (goal setting) - start on the Sunday before thread creation
                var threadDate = DateOnly.FromDateTime(startWeekThread.CreatedAt);
                var daysSinceSunday = ((int)startWeekThread.CreatedAt.DayOfWeek) % 7;
                calculatedStartDate = threadDate.AddDays(-daysSinceSunday);
            }
            else
            {
                // Week 1 - start on the Monday of the week the thread was created
                var threadDate = DateOnly.FromDateTime(startWeekThread.CreatedAt);
                var daysSinceMonday = (int)startWeekThread.CreatedAt.DayOfWeek - 1;
                if (daysSinceMonday < 0) daysSinceMonday = 6; // Sunday wraps to 6
                calculatedStartDate = threadDate.AddDays(-daysSinceMonday);
            }

            // Calculate end date based on the highest week number
            var maxWeekNumber = sortedThreads.Max(t => t.WeekNumber);
            var durationWeeks = maxWeekNumber > 0 ? maxWeekNumber : 1; // At least 1 week
            
            // End date is the Sunday after the last week completes
            var calculatedEndDate = calculatedStartDate.AddDays((durationWeeks * 7) - 1);

            logger.LogInformation("Calculated challenge dates from {ThreadCount} threads: Start {StartDate}, End {EndDate} (Week 0-{MaxWeek})", 
                threads.Count, calculatedStartDate, calculatedEndDate, maxWeekNumber);

            return (calculatedStartDate, calculatedEndDate);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to calculate date range from threads");
            return (null, null);
        }
    }

    private async Task CreateWeekRecordsFromThreadsAsync(int challengeId, List<ThreadInfo> threads)
    {
        foreach (var thread in threads)
        {
            var week = new Week
            {
                ChallengeId = challengeId,
                WeekNumber = thread.WeekNumber,
                ThreadId = thread.ThreadId,
                LeaderboardPosted = false
            };
            context.Weeks.Add(week);
        }

        await context.SaveChangesAsync();
        logger.LogInformation("Created {WeekCount} week records for challenge {ChallengeId}", threads.Count, challengeId);
    }

    private async Task<(int messagesProcessed, List<ulong> usersFound)> ProcessHistoricalMessagesFromThreadAsync(
        MessageProcessorService messageProcessor, ulong threadId, int challengeId, int weekNumber)
    {
        try
        {
            var result = await messageProcessor.ProcessHistoricalMessagesAsync(threadId, challengeId, weekNumber);
            logger.LogInformation("Processed {MessageCount} historical messages from thread {ThreadId} for week {WeekNumber}", 
                result.messagesProcessed, threadId, weekNumber);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process historical messages from thread {ThreadId}", threadId);
            return (0, new List<ulong>());
        }
    }

    // Helper class for thread information during import
    private class ThreadInfo
    {
        public string Name { get; set; } = "";
        public ulong ThreadId { get; set; }
        public int WeekNumber { get; set; }
        public DateTime CreatedAt { get; set; }
    }
} 