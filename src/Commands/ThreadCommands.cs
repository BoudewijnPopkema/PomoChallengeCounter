using NetCord.Services.ApplicationCommands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PomoChallengeCounter.Data;
using PomoChallengeCounter.Services;
using PomoChallengeCounter.Models;

namespace PomoChallengeCounter.Commands;

public class ThreadCommands(
    ILocalizationService localizationService,
    PomoChallengeDbContext dbContext,
    IEmojiService emojiService,
    IDiscordThreadService discordThreadService,
    IChallengeService challengeService,
    ILogger<ThreadCommands> logger) : BaseCommand<ThreadCommands>(localizationService, dbContext, emojiService, logger)
{
    private readonly IDiscordThreadService _discordThreadService = discordThreadService;
    private readonly IChallengeService _challengeService = challengeService;

    [SlashCommand("thread-create", "Create a new week thread")]
    public async Task CreateThreadAsync(
        [SlashCommandParameter(Name = "week_number", Description = "Week number")] int weekNumber,
        [SlashCommandParameter(Name = "challenge_id", Description = "Challenge ID (optional)")] int? challengeId = null)
    {
        if (!await CheckPermissionsAsync(PermissionLevel.Config))
            return;

        try
        {
            var server = await DbContext.Servers.FindAsync(Context.Guild.Id);
            if (server == null)
            {
                await RespondAsync(GetLocalizedText("errors.server_not_setup"), ephemeral: true);
                return;
            }

            if (!server.CategoryId.HasValue)
            {
                await RespondAsync(GetLocalizedText("errors.category_not_configured"), ephemeral: true);
                return;
            }

            // Get the challenge (current or specified)
            var challenge = challengeId.HasValue 
                ? await _challengeService.GetChallengeAsync(challengeId.Value)
                : await _challengeService.GetCurrentChallengeAsync(Context.Guild.Id);

            if (challenge == null)
            {
                await RespondAsync(GetLocalizedText("errors.challenge_not_found"), ephemeral: true);
                return;
            }

            // Validate week number
            if (weekNumber < 0 || weekNumber > challenge.WeekCount)
            {
                await RespondAsync(GetLocalizedText("errors.invalid_week_number", weekNumber, challenge.WeekCount), ephemeral: true);
                return;
            }

            // Check if week already exists
            var existingWeek = await DbContext.Weeks
                .FirstOrDefaultAsync(w => w.ChallengeId == challenge.Id && w.WeekNumber == weekNumber);

            if (existingWeek != null && existingWeek.ThreadId != 0)
            {
                await RespondAsync(GetLocalizedText("errors.week_already_exists", weekNumber), ephemeral: true);
                return;
            }

            await DeferAsync(); // Thread creation might take a moment

            // Create thread name with proper goal suffix for week 0
            string threadName;
            if (weekNumber == 0)
            {
                // Week 0 is for goal setting - use localized "inzet" pattern
                var goalThreadSuffix = server.Language == "nl" ? "inzet" : "goals";
                threadName = $"Q{challenge.SemesterNumber}-{goalThreadSuffix}";
            }
            else
            {
                // Regular week threads
                threadName = $"Q{challenge.SemesterNumber}-week{weekNumber}";
            }
            
            // Create welcome message
            var welcomeMessages = GetLocalizedText("responses.welcome_messages").Split('\n');
            var randomWelcome = welcomeMessages[Random.Shared.Next(welcomeMessages.Length)];
            var welcomeMessage = string.Format(randomWelcome, weekNumber);

            // Create the Discord thread
            var threadResult = await _discordThreadService.CreateThreadAsync(
                Context.Guild.Id,
                server.CategoryId.Value,
                threadName,
                weekNumber,
                welcomeMessage,
                server.PingRoleId);

            if (!threadResult.IsSuccess)
            {
                await FollowupAsync(GetLocalizedText("errors.failed_to_create_thread", threadResult.ErrorMessage), ephemeral: true);
                return;
            }

            // Create or update week record
            if (existingWeek == null)
            {
                var week = new Week
                {
                    ChallengeId = challenge.Id,
                    WeekNumber = weekNumber,
                    ThreadId = threadResult.ThreadId,
                    LeaderboardPosted = false
                };
                await DbContext.Weeks.AddAsync(week);
            }
            else
            {
                existingWeek.ThreadId = threadResult.ThreadId;
            }

            await DbContext.SaveChangesAsync();

            await FollowupAsync(GetLocalizedText("responses.thread_created", threadName, $"<#{threadResult.ThreadId}>"));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error creating thread for guild {GuildId} - WeekNumber: {WeekNumber}, ChallengeId: {ChallengeId}", 
                Context.Guild.Id, weekNumber, challengeId);
            await FollowupAsync(GetLocalizedText("errors.error_creating_thread", ex.Message), ephemeral: true);
        }
    }

    [SlashCommand("thread-ping", "Ping the configured role in current thread")]
    public async Task PingAsync()
    {
        if (!await CheckPermissionsAsync(PermissionLevel.Config))
            return;

        try
        {
            var server = await DbContext.Servers.FindAsync(Context.Guild.Id);
            if (server == null)
            {
                await RespondAsync(GetLocalizedText("errors.server_not_setup"), ephemeral: true);
                return;
            }

            if (!server.PingRoleId.HasValue)
            {
                await RespondAsync("No ping role configured for this server.", ephemeral: true);
                return;
            }

            await RespondAsync($"📢 <@&{server.PingRoleId.Value}> Let's focus!");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error pinging role for guild {GuildId}", Context.Guild.Id);
            await RespondAsync(GetLocalizedText("errors.error_pinging_role", ex.Message), ephemeral: true);
        }
    }
} 