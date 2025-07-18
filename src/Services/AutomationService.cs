using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using PomoChallengeCounter.Data;
using PomoChallengeCounter.Models;
using PomoChallengeCounter.Models.Results;

namespace PomoChallengeCounter.Services;

public class AutomationService(
    IServiceProvider serviceProvider,
    ITimeProvider timeProvider,
    GatewayClient gatewayClient,
    ILocalizationService localizationService,
    ILogger<AutomationService> logger) : BackgroundService
{
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(15); // Check every 15 minutes

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Automation service started - checking for weekly thread creation every {Interval}", _checkInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndCreateWeeklyThreadsAsync();
                await CheckAndPostLeaderboardsAsync();
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Automation service cancelled");
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in automation service");
                // Continue running even if there's an error
                await Task.Delay(_checkInterval, stoppingToken);
            }
        }
    }

    private async Task CheckAndCreateWeeklyThreadsAsync()
    {
        var utcNow = timeProvider.UtcNow;
        
        // Convert to Amsterdam time (Central European Time)
        var amsterdamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
        var amsterdamNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, amsterdamTimeZone);
        
        // Check if it's Monday and between 9:00 AM and 9:15 AM Amsterdam time (our check window)
        if (amsterdamNow.DayOfWeek != DayOfWeek.Monday)
            return;

        var timeOfDay = amsterdamNow.TimeOfDay;
        if (timeOfDay < TimeSpan.FromHours(9) || timeOfDay >= TimeSpan.FromHours(9.25))
            return;

        logger.LogInformation("Monday 9am Amsterdam time detected - checking for weekly thread creation needs");

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PomoChallengeDbContext>();
        var challengeService = scope.ServiceProvider.GetRequiredService<IChallengeService>();

        // Get all active challenges that need weekly threads
        var activeChallenges = await context.Challenges
            .Include(c => c.Server)
            .Include(c => c.Weeks)
            .Where(c => c.IsActive && c.IsCurrent)
            .Where(c => c.StartDate <= DateOnly.FromDateTime(amsterdamNow) && c.EndDate >= DateOnly.FromDateTime(amsterdamNow))
            .ToListAsync();

        logger.LogInformation("Found {Count} active challenges to check for thread creation", activeChallenges.Count);

        foreach (var challenge in activeChallenges)
        {
            try
            {
                await ProcessChallengeThreadCreationAsync(challenge, amsterdamNow, challengeService);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process thread creation for challenge {ChallengeId} in server {ServerId}", 
                    challenge.Id, challenge.ServerId);
            }
        }
    }

    private async Task ProcessChallengeThreadCreationAsync(
        Challenge challenge, 
        DateTime now, 
        IChallengeService challengeService)
    {
        // Calculate which week we should be in
        var challengeStart = challenge.StartDate.ToDateTime(TimeOnly.MinValue);
        var daysSinceStart = (now.Date - challengeStart.Date).Days;
        var currentWeekNumber = (daysSinceStart / 7) + 1;

        // Check if we already have a thread for this week
        var existingWeek = challenge.Weeks.FirstOrDefault(w => w.WeekNumber == currentWeekNumber);
        if (existingWeek != null && existingWeek.ThreadId != 0)
        {
            logger.LogDebug("Week {WeekNumber} thread already exists for challenge {ChallengeId}", 
                currentWeekNumber, challenge.Id);
            return;
        }

        // Check if this week is within the challenge duration
        if (currentWeekNumber > challenge.WeekCount)
        {
            logger.LogDebug("Week {WeekNumber} is beyond challenge duration ({WeekCount}) for challenge {ChallengeId}", 
                currentWeekNumber, challenge.WeekCount, challenge.Id);
            return;
        }

        logger.LogInformation("Creating weekly thread for challenge {ChallengeId}, week {WeekNumber}", 
            challenge.Id, currentWeekNumber);

        // Create the weekly thread (CreateDiscordThreadAsync will handle week record creation)
        try
        {
            await CreateDiscordThreadAsync(challenge, currentWeekNumber, existingWeek);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create week record for challenge {ChallengeId}, week {WeekNumber}", 
                challenge.Id, currentWeekNumber);
        }
    }

    private async Task CreateDiscordThreadAsync(Challenge challenge, int weekNumber, Week? existingWeek)
    {
        try
        {
            logger.LogInformation("Creating Discord thread for challenge {ChallengeId}, week {WeekNumber}", challenge.Id, weekNumber);

            // Validate that server has category configured
            if (!challenge.Server.CategoryId.HasValue)
            {
                logger.LogWarning("No category configured for server {ServerId}, cannot create thread", challenge.ServerId);
                await CreateWeekRecord(challenge.Id, weekNumber, 0, existingWeek, weekNumber == 0);
                return;
            }

            // Create the thread name based on week number
            string threadName;
            if (weekNumber == 0)
            {
                // Week 0 is for goal setting - use localized "inzet" pattern
                var serverLanguage = challenge.Server.Language;
                var goalThreadSuffix = serverLanguage == "nl" ? "inzet" : "goals";
                threadName = $"Q{challenge.SemesterNumber}-{goalThreadSuffix}";
            }
            else
            {
                // Regular week threads
                threadName = $"Q{challenge.SemesterNumber}-week{weekNumber}";
            }

            // Use the DiscordThreadService to create challenge thread (will create channel if needed)
            using var scope = serviceProvider.CreateScope();
            var discordThreadService = scope.ServiceProvider.GetRequiredService<IDiscordThreadService>();
            
            // Get welcome message
            var welcomeMessage = GetRandomWelcomeMessage(challenge.Server.Language, weekNumber);
            
            // Create challenge thread using the proper service
            var threadResult = await discordThreadService.CreateChallengeThreadAsync(
                challenge.ServerId,
                challenge.Server.CategoryId.Value,
                threadName,
                weekNumber,
                challenge.Theme,
                challenge.SemesterNumber,
                welcomeMessage,
                challenge.Server.PingRoleId);
            
            if (threadResult.IsSuccess)
            {
                logger.LogInformation("Successfully created challenge thread {ThreadName} (ID: {ThreadId}) for Q{SemesterNumber}-week{WeekNumber}",
                    threadName, threadResult.ThreadId, challenge.SemesterNumber, weekNumber);
                
                // Create or update week record with actual thread ID
                await CreateWeekRecord(challenge.Id, weekNumber, threadResult.ThreadId, existingWeek, weekNumber == 0);
            }
            else
            {
                logger.LogError("Failed to create challenge thread {ThreadName}: {Error}", threadName, threadResult.ErrorMessage);
                // Still create week record for database consistency, but with no thread ID
                await CreateWeekRecord(challenge.Id, weekNumber, 0, existingWeek, weekNumber == 0);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create Discord thread for week {WeekNumber} in challenge {ChallengeId}", 
                weekNumber, challenge.Id);
        }
    }

    private async Task CheckAndPostLeaderboardsAsync()
    {
        var utcNow = timeProvider.UtcNow;
        
        // Convert to Amsterdam time (Central European Time)
        var amsterdamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
        var amsterdamNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, amsterdamTimeZone);
        
        // Check if it's Tuesday and between 12:00 PM and 12:15 PM Amsterdam time (our check window)
        if (amsterdamNow.DayOfWeek != DayOfWeek.Tuesday)
            return;

        var timeOfDay = amsterdamNow.TimeOfDay;
        if (timeOfDay < TimeSpan.FromHours(12) || timeOfDay >= TimeSpan.FromHours(12.25))
            return;

        logger.LogInformation("Tuesday 12pm Amsterdam time detected - checking for weekly leaderboard posting needs");

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PomoChallengeDbContext>();
        var challengeService = scope.ServiceProvider.GetRequiredService<IChallengeService>();

        // Get all weeks that need leaderboard posting
        var weeksNeedingLeaderboards = await context.Weeks
            .Include(w => w.Challenge)
            .ThenInclude(c => c.Server)
            .Where(w => !w.LeaderboardPosted && w.ThreadId != 0)
            .Where(w => w.Challenge.IsActive && w.Challenge.IsCurrent)
            .ToListAsync();

        logger.LogInformation("Found {Count} weeks that need leaderboard posting", weeksNeedingLeaderboards.Count);

        foreach (var week in weeksNeedingLeaderboards)
        {
            try
            {
                await ProcessLeaderboardPostingAsync(week, amsterdamNow, challengeService);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to post leaderboard for week {WeekId} in challenge {ChallengeId}", 
                    week.Id, week.ChallengeId);
            }
        }
    }

    private async Task ProcessLeaderboardPostingAsync(
        Week week, 
        DateTime now, 
        IChallengeService challengeService)
    {
        // Calculate if this week should have ended (it's the Tuesday after the week ended)
        var challenge = week.Challenge;
        
        // Convert challenge dates to Amsterdam time for consistent comparison  
        var challengeStartAmsterdam = ConvertDateOnlyToAmsterdamTime(challenge.StartDate);
        var weekStartDate = challengeStartAmsterdam.AddDays((week.WeekNumber - 1) * 7);
        var weekEndDate = weekStartDate.AddDays(7); // End of the week (Amsterdam time)
        var tuesdayAfter = weekEndDate.AddDays(1); // Tuesday after the week
        
        // Only post leaderboard on the Tuesday after the week has ended (both times in Amsterdam timezone)
        if (now.Date != tuesdayAfter.Date)
        {
            logger.LogDebug("Week {WeekNumber} in challenge {ChallengeId} hasn't ended yet or it's not the right Tuesday, skipping leaderboard posting", 
                week.WeekNumber, challenge.Id);
            return;
        }

        logger.LogInformation("Posting leaderboard for week {WeekNumber} in challenge {ChallengeId}", 
            week.WeekNumber, challenge.Id);

        try
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PomoChallengeDbContext>();
            var messageProcessor = scope.ServiceProvider.GetRequiredService<MessageProcessorService>();

            // 1. Rescan the entire week before leaderboard generation to ensure accuracy
            // Skip rescanning if Discord client is not available (test environment)
            if (gatewayClient?.Rest != null)
            {
                logger.LogInformation("Rescanning week {WeekNumber} before leaderboard generation", week.WeekNumber);
                
                // Get all current messages from the thread to rescan
                var threadMessages = new List<(ulong MessageId, ulong UserId, string Content)>();
                
                try
                {
                    var channel = await gatewayClient.Rest.GetChannelAsync(week.ThreadId);
                    if (channel is NetCord.TextChannel textChannel)
                    {
                        await foreach (var message in textChannel.GetMessagesAsync())
                        {
                            // Skip bot messages
                            if (message.Author.IsBot)
                                continue;
                                
                            threadMessages.Add((message.Id, message.Author.Id, message.Content ?? string.Empty));
                        }
                        
                        logger.LogInformation("Found {MessageCount} messages to rescan for week {WeekNumber}", 
                            threadMessages.Count, week.WeekNumber);
                    }
                }
                catch (Exception rescanEx)
                {
                    logger.LogWarning(rescanEx, "Failed to fetch messages for week rescan, proceeding with existing data");
                }
                
                // Perform the rescan if we have messages
                if (threadMessages.Any())
                {
                    try
                    {
                        var rescanResult = await messageProcessor.RescanWeekAsync(week.Id, threadMessages);
                        logger.LogInformation("Week rescan completed: {Processed} processed, {Skipped} skipped, {Deleted} deleted", 
                            rescanResult.ProcessedCount, rescanResult.SkippedCount, rescanResult.DeletedCount);
                    }
                    catch (Exception rescanEx2)
                    {
                        logger.LogWarning(rescanEx2, "Week rescan failed, proceeding with existing data");
                    }
                }
                else
                {
                    logger.LogInformation("No messages found for week {WeekNumber}, proceeding with existing data", week.WeekNumber);
                }
            }
            else
            {
                logger.LogDebug("Discord client not available, skipping week rescan");
            }

            // 2. Generate leaderboard embed using the reusable method
            var embed = await messageProcessor.GenerateLeaderboardEmbedAsync(week.Id);
            
            // Check if there's any leaderboard data by looking at the embed description
            if (embed.Description?.Contains("No data found") == true)
            {
                logger.LogInformation("No data found for leaderboard of week {WeekNumber} in challenge {ChallengeId}", 
                    week.WeekNumber, challenge.Id);
                    
                // Still mark as posted to prevent retrying
                var weekToUpdate = await context.Weeks.FindAsync(week.Id);
                if (weekToUpdate != null)
                {
                    weekToUpdate.LeaderboardPosted = true;
                    await context.SaveChangesAsync();
                }
                return;
            }

            // 3. Post to the Discord thread
            if (week.ThreadId != 0)
            {
                if (gatewayClient?.Rest != null)
                {
                    try
                    {
                        var channel = await gatewayClient.Rest.GetChannelAsync(week.ThreadId);
                        if (channel is TextGuildChannel textChannel)
                        {
                            var message = new MessageProperties()
                            {
                                Embeds = [embed]
                            };

                            await textChannel.SendMessageAsync(message);
                            
                            logger.LogInformation("Successfully posted leaderboard for week {WeekNumber} in challenge {ChallengeId} to thread {ThreadId}",
                                week.WeekNumber, challenge.Id, week.ThreadId);
                        }
                        else
                        {
                            logger.LogWarning("Channel {ThreadId} is not a text channel, cannot post leaderboard", week.ThreadId);
                        }
                    }
                    catch (Exception threadEx)
                    {
                        logger.LogError(threadEx, "Failed to post leaderboard message to thread {ThreadId}", week.ThreadId);
                        // Don't mark as posted if Discord posting failed
                        return;
                    }
                }
                else
                {
                    logger.LogInformation("Discord client not available (test mode), skipping actual Discord posting for week {WeekNumber}", week.WeekNumber);
                }
            }
            else
            {
                logger.LogWarning("Week {WeekNumber} in challenge {ChallengeId} has no thread ID, cannot post leaderboard",
                    week.WeekNumber, challenge.Id);
                // Don't mark as posted if there's no thread
                return;
            }

            // 4. Mark week.LeaderboardPosted = true only if Discord posting succeeded
            var weekToMarkPosted = await context.Weeks.FindAsync(week.Id);
            if (weekToMarkPosted != null)
            {
                weekToMarkPosted.LeaderboardPosted = true;
                await context.SaveChangesAsync();
                
                logger.LogInformation("Marked leaderboard as posted for week {WeekNumber} in challenge {ChallengeId}", 
                    week.WeekNumber, challenge.Id);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to post leaderboard for week {WeekNumber} in challenge {ChallengeId}", 
                week.WeekNumber, challenge.Id);
        }
    }

    // NOTE: Removed duplicate CreateLeaderboardEmbedAsync method
    // The actual implementation is in MessageProcessorService.GenerateLeaderboardEmbedAsync()

    // NOTE: Removed duplicate GetRankEmoji method
    // The actual implementation is in MessageProcessorService.GetRankEmoji()

    private async Task CreateWeekRecord(int challengeId, int weekNumber, ulong threadId, Week? existingWeek, bool isGoalThread = false)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PomoChallengeDbContext>();
        
        if (existingWeek == null)
        {
            var week = new Week
            {
                ChallengeId = challengeId,
                WeekNumber = weekNumber,
                ThreadId = isGoalThread ? 0 : threadId,  // Regular thread ID for non-goal threads
                GoalThreadId = isGoalThread ? threadId : null,  // Goal thread ID for week 0
                LeaderboardPosted = false
            };
            await context.Weeks.AddAsync(week);
            await context.SaveChangesAsync();
            
            var threadType = isGoalThread ? "Goal" : "Regular";
            logger.LogInformation("Created {ThreadType} week record: Challenge {ChallengeId}, Week {WeekNumber}, ThreadId {ThreadId}",
                threadType, challengeId, weekNumber, threadId);
        }
        else
        {
            // Update existing week with appropriate thread ID
            if (isGoalThread)
        {
                existingWeek.GoalThreadId = threadId;
            }
            else
            {
            existingWeek.ThreadId = threadId;
            }
            
            context.Weeks.Update(existingWeek);
            await context.SaveChangesAsync();
            
            var threadType = isGoalThread ? "Goal" : "Regular";
            logger.LogInformation("Updated week record with {ThreadType} ThreadId {ThreadId}: Challenge {ChallengeId}, Week {WeekNumber}",
                threadType, threadId, challengeId, weekNumber);
        }
    }

    private async Task SendThreadWelcomeMessageAsync(GuildThread thread, Challenge challenge)
    {
        try
        {
            // Get localized welcome messages based on server language
            var serverLanguage = challenge.Server.Language;
            var message = GetRandomWelcomeMessage(serverLanguage, GetCurrentWeekNumber(challenge));
            
            // Add role ping if configured
            if (challenge.Server.ConfigRoleId.HasValue)
            {
                message = $"<@&{challenge.Server.ConfigRoleId.Value}> {message}";
            }
            
            // Send the welcome message to the thread
            await thread.SendMessageAsync(message);
            
            logger.LogInformation("Sent welcome message to thread {ThreadName}: '{Message}' (language: {Language}, role ping: {HasRolePing})", 
                thread.Name, message, serverLanguage, challenge.Server.ConfigRoleId.HasValue);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send welcome message to thread {ThreadId}", thread.Id);
        }
    }
    
    private string GetRandomWelcomeMessage(string language, int weekNumber)
    {
        try
        {
            // Get the welcome messages string from localization
            var welcomeMessagesString = localizationService.GetString("responses.welcome_messages", language);
            
            // Parse the newline-separated string (fallback to hardcoded messages if parsing fails)
            var welcomeMessages = ParseWelcomeMessages(welcomeMessagesString, language);
            
            var random = new Random();
            var selectedMessage = welcomeMessages[random.Next(welcomeMessages.Length)];
            
            // Format the message with week number if it contains {0}
            return selectedMessage.Contains("{0}") 
                ? string.Format(selectedMessage, weekNumber)
                : selectedMessage;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get localized welcome message, using fallback");
            return language == "nl" 
                ? "Nieuwe week, tijd om te studeren! Laten we dit doen 🍅"
                : "It's a new week, let's get studying! 🍅";
        }
    }
    
    private string[] ParseWelcomeMessages(string welcomeMessagesString, string language)
    {
        // If we get the key back instead of actual messages, use fallback
        if (welcomeMessagesString == "responses.welcome_messages")
        {
            return language == "nl" ? 
                new[] {
                    "Nieuwe week, tijd om te studeren! Laten we dit doen 🍅",
                    "Week {0} is begonnen - tijd om te blokken! 💪",
                    "Fresh week vibes! Op naar die studiesessies 🔥",
                    "Nieuwe week, nieuwe kansen om te leren! ✨",
                    "Laten we deze week weer lekker studeren! 🚀",
                    "Time voor focus en kennis opdoen! ⚡",
                    "Nieuwe week = nieuwe leerstof! Let's go 🎯",
                    "Klaar voor een week vol studeren? Laten we knallen! 💯"
                } :
                new[] {
                    "It's a new week, let's get studying! 🍅",
                    "Fresh week, time to hit the books! 💪",
                    "New week = new study goals! Let's crush them 🔥",
                    "Week {0} is here - time to ace those study sessions! ✨",
                    "Another week, another chance to master that material! 🚀",
                    "Time to turn those study sessions into pomodoro power! ⚡",
                    "New week vibes - let's make studying fun! 🎯",
                    "Ready to tackle some serious study time? Let's go! 💯"
                };
        }
        
        // Parse newline-separated string from localization
        var messages = welcomeMessagesString.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return messages.Length > 0 ? messages : new[] { "New week, let's study! 🍅" };
    }
    
    private int GetCurrentWeekNumber(Challenge challenge)
    {
        var utcNow = timeProvider.UtcNow;
        var amsterdamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
        var amsterdamNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, amsterdamTimeZone);
        
        // Convert challenge start date to Amsterdam time for consistent comparison
        var challengeStartAmsterdam = ConvertDateOnlyToAmsterdamTime(challenge.StartDate);
        var weeksSinceStart = (amsterdamNow.Date - challengeStartAmsterdam.Date).Days / 7 + 1;
        
        return Math.Max(1, weeksSinceStart);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Automation service stopping");
        return base.StopAsync(cancellationToken);
    }
    
    /// <summary>
    /// Convert a DateOnly to DateTime in Amsterdam timezone for consistent scheduling comparisons
    /// </summary>
    private DateTime ConvertDateOnlyToAmsterdamTime(DateOnly date)
    {
        var amsterdamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
        
        // Create DateTime at midnight and specify it as Amsterdam time
        var dateTime = date.ToDateTime(TimeOnly.MinValue);
        var amsterdamDateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified);
        
        // Convert to UTC first, then back to Amsterdam to ensure proper timezone handling
        var utcDateTime = TimeZoneInfo.ConvertTimeToUtc(amsterdamDateTime, amsterdamTimeZone);
        return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, amsterdamTimeZone);
    }
} 