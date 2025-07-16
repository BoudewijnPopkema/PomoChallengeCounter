using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Microsoft.EntityFrameworkCore;
using PomoChallengeCounter.Data;
using PomoChallengeCounter.Services;

namespace PomoChallengeCounter.Commands;

public class ThreadCommands : ApplicationCommandModule<ApplicationCommandContext>
{
    public LocalizationService LocalizationService { get; set; } = null!;
    public PomoChallengeDbContext DbContext { get; set; } = null!;

    protected async Task<bool> CheckPermissionsAsync(PermissionLevel requiredLevel)
    {
        // TODO: Implement proper NetCord permissions checking
        // For now, allow all commands to proceed
        return true;
    }

    protected string GetLocalizedText(string key, params object[] args)
    {
        return LocalizationService.GetString(key, GetServerLanguage(), args);
    }

    private string GetServerLanguage()
    {
        if (Context.Guild == null) return "en";

        var server = DbContext.Servers
            .AsNoTracking()
            .FirstOrDefault(s => s.Id == Context.Guild.Id);

        return server?.Language ?? "en";
    }

    protected async Task RespondAsync(string content, bool ephemeral = false)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new()
        {
            Content = content,
            Flags = ephemeral ? MessageFlags.Ephemeral : null
        }));
    }

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

    [SlashCommand("leaderboard", "Generate a leaderboard for the current week")]
    public async Task LeaderboardAsync()
    {
        try
        {
            var server = await DbContext.Servers.FindAsync(Context.Guild.Id);
            if (server == null)
            {
                await RespondAsync(GetLocalizedText("errors.server_not_setup"), ephemeral: true);
                return;
            }

            // TODO: Implement proper leaderboard generation
            var embed = new EmbedProperties()
                .WithTitle("🏆 Weekly Leaderboard")
                .WithColor(new Color(0xffd700))
                .WithDescription("Leaderboard generation is being implemented for NetCord");

            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new()
            {
                Embeds = [embed]
            }));
        }
        catch (Exception ex)
        {
            await RespondAsync($"Error generating leaderboard: {ex.Message}", ephemeral: true);
        }
    }

    // TODO: Re-implement complex thread and leaderboard logic after basic NetCord integration is working
} 