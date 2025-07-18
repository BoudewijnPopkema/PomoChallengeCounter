using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Microsoft.Extensions.Logging;
using PomoChallengeCounter.Data;
using PomoChallengeCounter.Models;
using PomoChallengeCounter.Services;

namespace PomoChallengeCounter.Commands;

public class SetupCommands(
    ILocalizationService localizationService,
    PomoChallengeDbContext dbContext,
    IEmojiService emojiService,
    ILogger<SetupCommands> logger) : BaseCommand<SetupCommands>(localizationService, dbContext, emojiService, logger)
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

            // Build success message
            var message = GetLocalizedText("responses.setup_success");
            message += $"\n**Category**: <#{category.Id}>";
            message += $"\n**Language**: {language.ToUpper()}";
            
            if (configRole != null)
                message += $"\n**Config Role**: <@&{configRole.Id}>";
            
            if (pingRole != null)
                message += $"\n**Ping Role**: <@&{pingRole.Id}>";

            await RespondAsync(message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error during server setup for guild {GuildId} - CategoryId: {CategoryId}, Language: {Language}, ConfigRoleId: {ConfigRoleId}, PingRoleId: {PingRoleId}", 
                Context.Guild.Id, category.Id, language, configRole?.Id, pingRole?.Id);
            await RespondAsync(GetLocalizedText("errors.setup_error", ex.Message), ephemeral: true);
        }
    }
} 