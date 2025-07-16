using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Microsoft.EntityFrameworkCore;
using PomoChallengeCounter.Data;
using PomoChallengeCounter.Services;
using System.Globalization;

namespace PomoChallengeCounter.Commands;

public class ChallengeCommands : ApplicationCommandModule<ApplicationCommandContext>
{
    public LocalizationService LocalizationService { get; set; } = null!;
    public PomoChallengeDbContext DbContext { get; set; } = null!;
    public IChallengeService ChallengeService { get; set; } = null!;

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

    protected async Task RespondWithErrorAsync(string key, params object[] args)
    {
        await RespondAsync(GetLocalizedText(key, args), true);
    }

    protected async Task RespondWithSuccessAsync(string key, params object[] args)
    {
        await RespondAsync(GetLocalizedText(key, args));
    }

    [SlashCommand("challenge-create", "Create a new pomodoro challenge")]
    public async Task CreateAsync(
        [SlashCommandParameter(Name = "semester", Description = "Semester number")] int semester,
        [SlashCommandParameter(Name = "theme", Description = "Challenge theme name")] string theme,
        [SlashCommandParameter(Name = "start_date", Description = "Start date (YYYY-MM-DD)")] string startDate,
        [SlashCommandParameter(Name = "end_date", Description = "End date (YYYY-MM-DD)")] string endDate,
        [SlashCommandParameter(Name = "weeks", Description = "Number of weeks")] int weeks)
    {
        if (!await CheckPermissionsAsync(PermissionLevel.Config))
            return;

        try
        {
            // Basic validation and creation
            var server = await DbContext.Servers.FindAsync(Context.Guild.Id);
            if (server == null)
            {
                await RespondWithErrorAsync("errors.server_not_setup");
                return;
            }

            // TODO: Implement proper challenge creation with date parsing
            await RespondWithSuccessAsync("Challenge creation is being implemented for NetCord");
        }
        catch (Exception ex)
        {
            await RespondAsync($"Error creating challenge: {ex.Message}", ephemeral: true);
        }
    }

    [SlashCommand("challenge-list", "List all challenges")]
    public async Task ListAsync()
    {
        try
        {
            var server = await DbContext.Servers.FindAsync(Context.Guild.Id);
            if (server == null)
            {
                await RespondWithErrorAsync("errors.server_not_setup");
                return;
            }

            var challenges = await DbContext.Challenges
                .Where(c => c.ServerId == server.Id)
                .OrderByDescending(c => c.SemesterNumber)
                .Take(10)
                .ToListAsync();

            if (!challenges.Any())
            {
                await RespondAsync("No challenges found for this server.");
                return;
            }

            var embed = new EmbedProperties()
                .WithTitle("📋 Server Challenges")
                .WithColor(new Color(0x0099ff))
                .WithDescription($"Found {challenges.Count} challenges:");

            foreach (var challenge in challenges)
            {
                embed.AddFields(new EmbedFieldProperties()
                    .WithName($"Q{challenge.SemesterNumber}: {challenge.Theme}")
                    .WithValue($"Status: {(challenge.IsActive ? "Active" : "Inactive")}")
                    .WithInline(true));
            }

            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new()
            {
                Embeds = [embed]
            }));
        }
        catch (Exception ex)
        {
            await RespondAsync($"Error listing challenges: {ex.Message}", ephemeral: true);
        }
    }

    [SlashCommand("challenge-info", "Get information about the current challenge")]
    public async Task InfoAsync()
    {
        try
        {
            var server = await DbContext.Servers.FindAsync(Context.Guild.Id);
            if (server == null)
            {
                await RespondWithErrorAsync("errors.server_not_setup");
                return;
            }

            var currentChallenge = await DbContext.Challenges
                .Where(c => c.ServerId == server.Id && c.IsActive)
                .FirstOrDefaultAsync();

            if (currentChallenge == null)
            {
                await RespondAsync("No active challenge found.");
                return;
            }

            var embed = new EmbedProperties()
                .WithTitle($"🎯 Current Challenge: Q{currentChallenge.SemesterNumber}")
                .WithColor(new Color(0x00ff00))
                .AddFields(
                    new EmbedFieldProperties()
                        .WithName("Theme")
                        .WithValue(currentChallenge.Theme)
                        .WithInline(false),
                    new EmbedFieldProperties()
                        .WithName("Status")
                        .WithValue($"Started: {currentChallenge.IsStarted} | Active: {currentChallenge.IsActive}")
                        .WithInline(true)
                );

            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new()
            {
                Embeds = [embed]
            }));
        }
        catch (Exception ex)
        {
            await RespondAsync($"Error getting challenge info: {ex.Message}", ephemeral: true);
        }
    }

    // TODO: Re-implement other challenge commands (start, stop, deactivate) after basic NetCord integration is working
} 