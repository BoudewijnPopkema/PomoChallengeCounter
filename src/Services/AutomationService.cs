using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using PomoChallengeCounter.Data;
using PomoChallengeCounter.Models;
using PomoChallengeCounter.Services;

namespace PomoChallengeCounter.Services;

public class AutomationService(
    IServiceProvider serviceProvider,
    ITimeProvider timeProvider,
    GatewayClient gatewayClient,
    LocalizationService localizationService,
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
        var utcNow = timeProvider.Now;
        
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

        // Create the weekly thread
        // Note: This would need to integrate with Discord thread creation
        // For now, we'll create the week record and mark it as needing a thread
        try
        {
            if (existingWeek == null)
            {
                // Create new week record
                var week = new Week
                {
                    ChallengeId = challenge.Id,
                    WeekNumber = currentWeekNumber,
                    ThreadId = 0, // Will be set when actual Discord thread is created
                    LeaderboardPosted = false
                };

                using var scope = serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<PomoChallengeDbContext>();
                await context.Weeks.AddAsync(week);
                await context.SaveChangesAsync();

                logger.LogInformation("Created week {WeekNumber} record for challenge {ChallengeId} - Discord thread creation pending", 
                    currentWeekNumber, challenge.Id);
            }
            else
            {
                logger.LogInformation("Week {WeekNumber} record exists but needs Discord thread creation for challenge {ChallengeId}", 
                    currentWeekNumber, challenge.Id);
            }

            // Create actual Discord thread
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
            // Get the Discord guild
            var guild = await gatewayClient.Rest.GetGuildAsync(challenge.ServerId);
            if (guild == null)
            {
                logger.LogError("Could not find guild {ServerId} for challenge {ChallengeId}", challenge.ServerId, challenge.Id);
                return;
            }

            logger.LogInformation("Creating Discord thread for challenge {ChallengeId}, week {WeekNumber}", challenge.Id, weekNumber);

            // Get guild channels to find the challenge channel
            var channels = await guild.GetChannelsAsync();
            
            // Find the challenge channel by name (look for challenge theme or Q# pattern)
            var challengeChannel = channels.FirstOrDefault(c => 
                c.Name?.Contains(challenge.Theme.ToLowerInvariant().Replace(" ", "-")) == true ||
                c.Name?.Contains($"q{challenge.SemesterNumber}") == true ||
                c.Name?.Contains("challenge") == true);

            if (challengeChannel == null)
            {
                logger.LogWarning("Could not find challenge channel for guild {ServerId}, challenge {ChallengeId}. Available channels: {Channels}",
                    challenge.ServerId, challenge.Id, string.Join(", ", channels.Select(c => c.Name)));
                    
                // Create week record anyway for database consistency
                await CreateWeekRecord(challenge.Id, weekNumber, 0, existingWeek);
                return;
            }

            // Create the thread name
            var threadName = $"Q{challenge.SemesterNumber}-week{weekNumber}";
            
            logger.LogInformation("Found challenge channel {ChannelName} ({ChannelId}), creating thread {ThreadName}",
                challengeChannel.Name, challengeChannel.Id, threadName);

            // Try to create the thread using NetCord
            try
            {
                // Attempt to create an actual Discord thread
                logger.LogInformation("Attempting to create Discord thread {ThreadName} in channel {ChannelName} ({ChannelId})",
                    threadName, challengeChannel.Name, challengeChannel.Id);

                // Create actual Discord thread using NetCord API
                if (challengeChannel is TextGuildChannel textChannel)
                {
                    // Create thread with NetCord's CreateGuildThreadAsync - simplified for now
                    var threadProperties = new NetCord.Rest.GuildThreadProperties(threadName);
                    
                    var createdThread = await textChannel.CreateGuildThreadAsync(threadProperties);
                    
                    logger.LogInformation("Successfully created Discord thread {ThreadName} (ID: {ThreadId}) in channel {ChannelName}",
                        threadName, createdThread.Id, challengeChannel.Name);
                    
                    // Create or update week record with actual thread ID
                    await CreateWeekRecord(challenge.Id, weekNumber, createdThread.Id, existingWeek);
                    
                    // Send welcome message to thread with role ping
                    await SendThreadWelcomeMessageAsync(createdThread, challenge);
                    
                    logger.LogInformation("Thread created and welcome message sent for Q{SemesterNumber}-week{WeekNumber}",
                        challenge.SemesterNumber, weekNumber);
                }
                else
                {
                    logger.LogWarning("Channel {ChannelName} is not a TextGuildChannel, cannot create thread", challengeChannel.Name);
                    // Fallback to placeholder for non-text channels
                    await CreateWeekRecord(challenge.Id, weekNumber, challengeChannel.Id, existingWeek);
                }
                
                logger.LogInformation("Thread creation and setup completed for week {WeekNumber} in challenge {ChallengeId}", 
                    weekNumber, challenge.Id);
            }
            catch (Exception threadEx)
            {
                logger.LogError(threadEx, "Failed to create Discord thread {ThreadName} in channel {ChannelName}: {Error}",
                    threadName, challengeChannel.Name, threadEx.Message);
                    
                // Still create week record for database consistency
                await CreateWeekRecord(challenge.Id, weekNumber, 0, existingWeek);
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
        var utcNow = timeProvider.Now;
        
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
        var challengeStart = challenge.StartDate.ToDateTime(TimeOnly.MinValue);
        var weekStartDate = challengeStart.AddDays((week.WeekNumber - 1) * 7);
        var weekEndDate = weekStartDate.AddDays(7); // End of the week
        
        // Only post leaderboard on the Tuesday after the week has ended
        if (now.Date < weekEndDate.Date.AddDays(1)) // Tuesday after the week
        {
            logger.LogDebug("Week {WeekNumber} in challenge {ChallengeId} hasn't ended yet, skipping leaderboard posting", 
                week.WeekNumber, challenge.Id);
            return;
        }

        logger.LogInformation("Posting leaderboard for week {WeekNumber} in challenge {ChallengeId}", 
            week.WeekNumber, challenge.Id);

        try
        {
            // TODO: Generate and post actual leaderboard to Discord thread
            // This would require:
            // 1. Calculate leaderboard for the specific week
            // 2. Format leaderboard message
            // 3. Post to the Discord thread (week.ThreadId)
            // 4. Mark week.LeaderboardPosted = true

            // For now, just mark as posted (placeholder until Discord integration)
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PomoChallengeDbContext>();
            
            var weekToUpdate = await context.Weeks.FindAsync(week.Id);
            if (weekToUpdate != null)
            {
                weekToUpdate.LeaderboardPosted = true;
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

    private async Task CreateWeekRecord(int challengeId, int weekNumber, ulong threadId, Week? existingWeek)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PomoChallengeDbContext>();
        
        if (existingWeek == null)
        {
            var week = new Week
            {
                ChallengeId = challengeId,
                WeekNumber = weekNumber,
                ThreadId = threadId,
                LeaderboardPosted = false
            };
            await context.Weeks.AddAsync(week);
            await context.SaveChangesAsync();
            
            logger.LogInformation("Created week record: Challenge {ChallengeId}, Week {WeekNumber}, ThreadId {ThreadId}",
                challengeId, weekNumber, threadId);
        }
        else if (existingWeek.ThreadId != threadId)
        {
            // Update existing week with thread ID
            existingWeek.ThreadId = threadId;
            context.Weeks.Update(existingWeek);
            await context.SaveChangesAsync();
            
            logger.LogInformation("Updated week record with ThreadId {ThreadId}: Challenge {ChallengeId}, Week {WeekNumber}",
                threadId, challengeId, weekNumber);
        }
    }

    private async Task SendThreadWelcomeMessageAsync(GuildThread thread, Challenge challenge)
    {
        try
        {
            // Get localized welcome messages based on server language
            var serverLanguage = challenge.Server.Language ?? "en";
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
            // Get the welcome messages array from localization
            var welcomeMessagesJson = localizationService.GetString("responses.welcome_messages", language);
            
            // Parse the JSON array (fallback to hardcoded messages if parsing fails)
            var welcomeMessages = ParseWelcomeMessages(welcomeMessagesJson, language);
            
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
    
    private string[] ParseWelcomeMessages(string welcomeMessagesJson, string language)
    {
        // If we get the key back instead of actual messages, use fallback
        if (welcomeMessagesJson == "responses.welcome_messages")
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
        
        // For now, return fallback since JSON parsing of arrays from localization is complex
        return language == "nl" ? 
            new[] {
                "Nieuwe week, tijd om te studeren! Laten we dit doen 🍅",
                "Week {0} is begonnen - tijd om te blokken! 💪",
                "Fresh week vibes! Op naar die studiesessies 🔥",
                "Nieuwe week, nieuwe kansen om te leren! ✨",
                "Laten we deze week weer lekker studeren! 🚀"
            } :
            new[] {
                "It's a new week, let's get studying! 🍅",
                "Fresh week, time to hit the books! 💪",
                "New week = new study goals! Let's crush them 🔥",
                "Week {0} is here - time to ace those study sessions! ✨",
                "Another week, another chance to master that material! 🚀"
            };
    }
    
    private int GetCurrentWeekNumber(Challenge challenge)
    {
        var utcNow = timeProvider.Now;
        var amsterdamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
        var amsterdamNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, amsterdamTimeZone);
        
        var challengeStart = challenge.StartDate.ToDateTime(TimeOnly.MinValue);
        var weeksSinceStart = (amsterdamNow.Date - challengeStart.Date).Days / 7 + 1;
        
        return Math.Max(1, weeksSinceStart);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Automation service stopping");
        return base.StopAsync(cancellationToken);
    }
} 