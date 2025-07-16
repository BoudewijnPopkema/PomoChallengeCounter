using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Microsoft.EntityFrameworkCore;
using PomoChallengeCounter.Data;
using PomoChallengeCounter.Services;
using PomoChallengeCounter.Models;

namespace PomoChallengeCounter.Commands;

public class EmojiCommands : ApplicationCommandModule<ApplicationCommandContext>
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

    [SlashCommand("emoji-add", "Add a new emoji for point tracking")]
    public async Task AddAsync(
        [SlashCommandParameter(Name = "emoji", Description = "The emoji to add")] string emoji,
        [SlashCommandParameter(Name = "type", Description = "Emoji type")] string type,
        [SlashCommandParameter(Name = "points", Description = "Points value")] int points,
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

            // TODO: Implement proper emoji validation and creation
            await RespondAsync($"Emoji {emoji} addition is being implemented for NetCord");
        }
        catch (Exception ex)
        {
            await RespondAsync($"Error adding emoji: {ex.Message}", ephemeral: true);
        }
    }

    [SlashCommand("emoji-list", "List all configured emojis")]
    public async Task ListAsync()
    {
        try
        {
            var server = await DbContext.Servers.FindAsync(Context.Guild.Id);
            if (server == null)
            {
                await RespondAsync(GetLocalizedText("errors.server_not_setup"), ephemeral: true);
                return;
            }

                         var emojis = await DbContext.Emojis
                .Where(e => e.ServerId == server.Id && e.IsActive)
                .OrderBy(e => e.EmojiType)
                .ThenBy(e => e.EmojiCode)
                .ToListAsync();

            if (!emojis.Any())
            {
                await RespondAsync("No emojis configured for this server.");
                return;
            }

            var embed = new EmbedProperties()
                .WithTitle("😀 Server Emojis")
                .WithColor(new Color(0xffaa00))
                .WithDescription($"Found {emojis.Count} configured emojis:");

            foreach (var emoji in emojis.Take(10)) // Limit to prevent embed size issues
            {
                embed.AddFields(new EmbedFieldProperties()
                    .WithName($"{emoji.EmojiCode} ({emoji.EmojiType})")
                    .WithValue($"{emoji.PointValue} points")
                    .WithInline(true));
            }

            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new()
            {
                Embeds = [embed]
            }));
        }
        catch (Exception ex)
        {
            await RespondAsync($"Error listing emojis: {ex.Message}", ephemeral: true);
        }
    }

    [SlashCommand("emoji-remove", "Remove an emoji from tracking")]
    public async Task RemoveAsync(
        [SlashCommandParameter(Name = "emoji", Description = "The emoji to remove")] string emoji)
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

            // TODO: Implement proper emoji removal
            await RespondAsync($"Emoji {emoji} removal is being implemented for NetCord");
        }
        catch (Exception ex)
        {
            await RespondAsync($"Error removing emoji: {ex.Message}", ephemeral: true);
        }
    }

    // TODO: Re-implement emoji edit functionality after basic NetCord integration is working
} 