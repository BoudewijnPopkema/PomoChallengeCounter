using NetCord;
using NetCord.Services.ApplicationCommands;
using Microsoft.Extensions.Logging;
using PomoChallengeCounter.Data;
using PomoChallengeCounter.Services;

namespace PomoChallengeCounter.Commands;

public class ConfigCommands(
    ILocalizationService localizationService,
    PomoChallengeDbContext dbContext,
    IEmojiService emojiService,
    ILogger<ConfigCommands> logger) : BaseCommand<ConfigCommands>(localizationService, dbContext, emojiService, logger)
{
    [SlashCommand("config-category", "Set the category where challenges are created")]
    public async Task CategoryAsync(
        [SlashCommandParameter(Name = "category", Description = "Discord category for challenges")] Channel category)
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

            server.CategoryId = category.Id;
            await DbContext.SaveChangesAsync();

            await RespondAsync(GetLocalizedText("responses.category_updated", $"<#{category.Id}>"));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error setting category for guild {GuildId}, CategoryId: {CategoryId}", Context.Guild.Id, category.Id);
            await RespondAsync(GetLocalizedText("errors.config_category_error", ex.Message), ephemeral: true);
        }
    }

    [SlashCommand("config-language", "Set server language")]
    public async Task LanguageAsync(
        [SlashCommandParameter(Name = "language", Description = "Server language (en/nl)")] string language)
    {
        if (!await CheckPermissionsAsync(PermissionLevel.Config))
            return;

        try
        {
            // Validate language
            if (language != "en" && language != "nl")
            {
                await RespondAsync(GetLocalizedText("errors.invalid_language"), ephemeral: true);
                return;
            }

            var server = await DbContext.Servers.FindAsync(Context.Guild.Id);
            if (server == null)
            {
                await RespondAsync(GetLocalizedText("errors.server_not_setup"), ephemeral: true);
                return;
            }

            server.Language = language;
            await DbContext.SaveChangesAsync();

            await RespondAsync(GetLocalizedText("responses.language_updated", language.ToUpper()));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error setting language for guild {GuildId}, Language: {Language}", Context.Guild.Id, language);
            await RespondAsync($"Config language error: {ex.Message}", ephemeral: true);
        }
    }

    [SlashCommand("config-roles", "Configure permission and ping roles")]
    public async Task RolesAsync(
        [SlashCommandParameter(Name = "config_role", Description = "Role for bot configuration permissions")] Role? configRole = null,
        [SlashCommandParameter(Name = "ping_role", Description = "Role to ping for new threads")] Role? pingRole = null)
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

            if (configRole != null)
                server.ConfigRoleId = configRole.Id;

            if (pingRole != null)
                server.PingRoleId = pingRole.Id;

            await DbContext.SaveChangesAsync();

            var message = GetLocalizedText("responses.roles_updated");
            if (configRole != null)
                message += $"\nConfig Role: <@&{configRole.Id}>";
            if (pingRole != null)
                message += $"\nPing Role: <@&{pingRole.Id}>";

            await RespondAsync(message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error configuring roles for guild {GuildId}, ConfigRoleId: {ConfigRoleId}, PingRoleId: {PingRoleId}", 
                Context.Guild.Id, configRole?.Id, pingRole?.Id);
            await RespondAsync($"Config roles error: {ex.Message}", ephemeral: true);
        }
    }
} 