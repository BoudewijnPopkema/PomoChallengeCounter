using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Microsoft.EntityFrameworkCore;
using PomoChallengeCounter.Data;

namespace PomoChallengeCounter.Commands;

public class ThreadCommands : BaseCommand
{
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

            // TODO: Implement proper thread creation logic
            await RespondAsync($"Thread creation for week {weekNumber} is being implemented for NetCord");
        }
        catch (Exception ex)
        {
            await RespondAsync($"Error creating thread: {ex.Message}", ephemeral: true);
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
            await RespondAsync($"Error pinging role: {ex.Message}", ephemeral: true);
        }
    }

    // TODO: Re-implement complex thread and leaderboard logic after basic NetCord integration is working
} 