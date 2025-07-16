using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Microsoft.EntityFrameworkCore;
using PomoChallengeCounter.Models;

namespace PomoChallengeCounter.Commands;

public class SetupCommands : BaseCommand
{
    [SlashCommand("setup", "Initial bot setup for the server")]
    public async Task SetupAsync(
        [SlashCommandParameter(Name = "category", Description = "Discord category for challenges")] Channel category,
        [SlashCommandParameter(Name = "language", Description = "Server language (en/nl)")] string language = "en",
        [SlashCommandParameter(Name = "config_role", Description = "Role for bot configuration permissions")] Role? configRole = null,
        [SlashCommandParameter(Name = "ping_role", Description = "Role to ping for new threads")] Role? pingRole = null)
    {
        if (!await CheckPermissionsAsync(PermissionLevel.Admin))
            return;

        try
        {
            // Validate language
            if (language != "en" && language != "nl")
            {
                await RespondAsync(GetLocalizedText("errors.invalid_language"), ephemeral: true);
                return;
            }

            var guildId = Context.Guild.Id;
            
            // Check if server already exists
            var existingServer = await DbContext.Servers.FindAsync(guildId);
            if (existingServer != null)
            {
                await RespondAsync(GetLocalizedText("errors.server_already_setup"), ephemeral: true);
                return;
            }

            // Create new server configuration
            var server = new Server
            {
                Id = guildId,
                Name = Context.Guild.Name,
                Language = language,
                CategoryId = category.Id,
                ConfigRoleId = configRole?.Id,
                PingRoleId = pingRole?.Id
            };

            DbContext.Servers.Add(server);
            await DbContext.SaveChangesAsync();

            var embed = new EmbedProperties()
                .WithTitle("✅ Server Setup Complete")
                .WithColor(new Color(0x00ff00))
                .WithDescription(GetLocalizedText("setup.success_description"))
                .AddFields(
                    new EmbedFieldProperties()
                        .WithName(GetLocalizedText("setup.field_category"))
                        .WithValue($"<#{category.Id}>")
                        .WithInline(true),
                    new EmbedFieldProperties()
                        .WithName(GetLocalizedText("setup.field_language"))
                        .WithValue(language.ToUpper())
                        .WithInline(true),
                    new EmbedFieldProperties()
                        .WithName(GetLocalizedText("setup.field_config_role"))
                        .WithValue(configRole != null ? $"<@&{configRole.Id}>" : GetLocalizedText("setup.none"))
                        .WithInline(true),
                    new EmbedFieldProperties()
                        .WithName(GetLocalizedText("setup.field_ping_role"))
                        .WithValue(pingRole != null ? $"<@&{pingRole.Id}>" : GetLocalizedText("setup.none"))
                        .WithInline(true)
                );

            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new()
            {
                Embeds = [embed]
            }));
        }
        catch (Exception ex)
        {
            await RespondAsync($"Setup error: {ex.Message}", ephemeral: true);
        }
    }
} 