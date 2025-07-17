using NetCord.Services.ApplicationCommands;
using Microsoft.EntityFrameworkCore;
using PomoChallengeCounter.Services;
using PomoChallengeCounter.Models;

namespace PomoChallengeCounter.Commands;

public class ThreadCommands : BaseCommand
{
    public IDiscordThreadService DiscordThreadService { get; set; } = null!;
    public IChallengeService ChallengeService { get; set; } = null!;

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
                ? await ChallengeService.GetChallengeAsync(challengeId.Value)
                : await ChallengeService.GetCurrentChallengeAsync(Context.Guild.Id);

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

            // Create thread name
            var threadName = $"Q{challenge.SemesterNumber}-week{weekNumber}";
            
            // Create welcome message
            var welcomeMessages = GetLocalizedText("responses.welcome_messages").Split('\n');
            var randomWelcome = welcomeMessages[Random.Shared.Next(welcomeMessages.Length)];
            var welcomeMessage = string.Format(randomWelcome, weekNumber);

            // Create the Discord thread
            var threadResult = await DiscordThreadService.CreateThreadAsync(
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
            await RespondAsync(GetLocalizedText("errors.error_pinging_role", ex.Message), ephemeral: true);
        }
    }

    // TODO: Re-implement complex thread and leaderboard logic after basic NetCord integration is working
} 