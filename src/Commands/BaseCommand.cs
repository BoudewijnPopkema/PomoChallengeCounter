using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Microsoft.EntityFrameworkCore;
using PomoChallengeCounter.Data;
using PomoChallengeCounter.Services;

namespace PomoChallengeCounter.Commands;

public abstract class BaseCommand : ApplicationCommandModule<ApplicationCommandContext>
{
    public LocalizationService LocalizationService { get; set; } = null!;
    public PomoChallengeDbContext DbContext { get; set; } = null!;

    protected async Task<bool> CheckPermissionsAsync(PermissionLevel requiredLevel)
    {
        if (Context.Guild == null)
        {
            await RespondAsync(GetLocalizedText("errors.guild_only"), ephemeral: true);
            return false;
        }

        // Public commands are accessible to everyone
        if (requiredLevel == PermissionLevel.Public)
            return true;

        var user = Context.User;
        if (user == null)
        {
            await RespondAsync(GetLocalizedText("errors.user_not_found"), ephemeral: true);
            return false;
        }

        // Check if user is server owner (highest permission)
        if (user.Id == Context.Guild.OwnerId)
            return true;

        // Get user's roles in the guild
        var guildUser = Context.Guild.Users?.FirstOrDefault(u => u.Value.Id == user.Id).Value;
        if (guildUser == null)
        {
            await RespondAsync(GetLocalizedText("errors.user_not_in_guild"), ephemeral: true);
            return false;
        }

        var userRoles = guildUser.RoleIds?.ToList() ?? new List<ulong>();
        
        // For Config and Admin levels, check if user has the configured config role
        if (requiredLevel == PermissionLevel.Config || requiredLevel == PermissionLevel.Admin)
        {
            var server = await DbContext.Servers
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == Context.Guild.Id);

            if (server?.ConfigRoleId.HasValue == true && userRoles.Contains(server.ConfigRoleId.Value))
                return true;
        }

        // If we reach here, user doesn't have required permissions
        await RespondAsync(GetLocalizedText("errors.insufficient_permissions"), ephemeral: true);
        return false;
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

    protected async Task FollowupAsync(string content, bool ephemeral = false)
    {
        await Context.Interaction.SendFollowupMessageAsync(new()
        {
            Content = content,
            Flags = ephemeral ? MessageFlags.Ephemeral : null
        });
    }

    protected async Task DeferAsync(bool ephemeral = false)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(ephemeral ? MessageFlags.Ephemeral : null));
    }
}

public enum PermissionLevel
{
    Public,
    Config,
    Admin
} 