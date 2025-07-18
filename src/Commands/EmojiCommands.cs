using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PomoChallengeCounter.Data;
using PomoChallengeCounter.Models;
using PomoChallengeCounter.Services;

namespace PomoChallengeCounter.Commands;

public class EmojiCommands(
    ILocalizationService localizationService,
    PomoChallengeDbContext dbContext,
    IEmojiService emojiService,
    ILogger<EmojiCommands> logger) : BaseCommand<EmojiCommands>(localizationService, dbContext, emojiService, logger)
{
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
            if (Context.Guild == null)
            {
                await RespondAsync(GetLocalizedText("errors.guild_only"), ephemeral: true);
                return;
            }

            // Validate emoji format
            if (!EmojiService.ValidateEmojiFormat(emoji, out var format))
            {
                await RespondAsync(GetLocalizedText("errors.invalid_emoji_format"), ephemeral: true);
                return;
            }

            // Validate point value
            if (!EmojiService.ValidatePointValue(points))
            {
                await RespondAsync(GetLocalizedText("errors.invalid_point_value"), ephemeral: true);
                return;
            }

            // Validate emoji type
            if (!Enum.TryParse<EmojiType>(type, true, out var emojiType))
            {
                await RespondAsync(GetLocalizedText("errors.invalid_emoji_type"), ephemeral: true);
                return;
            }

            // Check if emoji already exists
            var existingEmoji = await DbContext.Emojis
                .FirstOrDefaultAsync(e => e.ServerId == Context.Guild.Id && e.EmojiCode == emoji && e.IsActive);

            if (existingEmoji != null)
            {
                await RespondAsync(GetLocalizedText("errors.emoji_already_exists"), ephemeral: true);
                return;
            }

            // Create new emoji
            var newEmoji = new Models.Emoji
            {
                ServerId = Context.Guild.Id,
                EmojiCode = emoji,
                EmojiType = emojiType,
                PointValue = points,
                ChallengeId = challengeId,
                IsActive = true
            };

            DbContext.Emojis.Add(newEmoji);
            await DbContext.SaveChangesAsync();

            var scope = challengeId.HasValue ? $"Challenge {challengeId}" : "Global";
            await RespondAsync($"✅ Emoji {emoji} added successfully!\n**Type**: {emojiType}\n**Points**: {points}\n**Scope**: {scope}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error adding emoji for guild {GuildId} - Emoji: {Emoji}, Type: {Type}, Points: {Points}, ChallengeId: {ChallengeId}", 
                Context.Guild.Id, emoji, type, points, challengeId);
            await RespondAsync($"❌ Error adding emoji: {ex.Message}", ephemeral: true);
        }
    }

    [SlashCommand("emoji-list", "List all configured emojis")]
    public async Task ListAsync()
    {
        if (!await CheckPermissionsAsync(PermissionLevel.Config))
            return;

        try
        {
            if (Context.Guild == null)
            {
                await RespondAsync(GetLocalizedText("errors.guild_only"), ephemeral: true);
                return;
            }

            var emojis = await DbContext.Emojis
                .Where(e => e.ServerId == Context.Guild.Id && e.IsActive)
                .OrderBy(e => e.EmojiType)
                .ThenBy(e => e.PointValue)
                .ToListAsync();

            if (!emojis.Any())
            {
                await RespondAsync("No emojis configured for this server.");
                return;
            }

            var embed = new EmbedProperties()
                .WithTitle("😀 Server Emojis")
                .WithColor(new Color(0xffd700));

            var pomodoros = emojis.Where(e => e.EmojiType == EmojiType.Pomodoro).ToList();
            var bonuses = emojis.Where(e => e.EmojiType == EmojiType.Bonus).ToList();
            var goals = emojis.Where(e => e.EmojiType == EmojiType.Goal).ToList();
            var rewards = emojis.Where(e => e.EmojiType == EmojiType.Reward).ToList();

            if (pomodoros.Any())
            {
                var pomoText = string.Join("\n", pomodoros.Select(e => $"{e.EmojiCode} = {e.PointValue} pts"));
                embed.AddFields(new EmbedFieldProperties()
                    .WithName("🍅 Pomodoro Emojis")
                    .WithValue(pomoText)
                    .WithInline(false));
            }

            if (bonuses.Any())
            {
                var bonusText = string.Join("\n", bonuses.Select(e => $"{e.EmojiCode} = {e.PointValue} pts"));
                embed.AddFields(new EmbedFieldProperties()
                    .WithName("⭐ Bonus Emojis")
                    .WithValue(bonusText)
                    .WithInline(false));
            }

            if (goals.Any())
            {
                var goalText = string.Join("\n", goals.Select(e => $"{e.EmojiCode} = {e.PointValue} pts"));
                embed.AddFields(new EmbedFieldProperties()
                    .WithName("🎯 Goal Emojis")
                    .WithValue(goalText)
                    .WithInline(false));
            }

            if (rewards.Any())
            {
                var rewardText = string.Join("\n", rewards.Select(e => $"{e.EmojiCode} = {e.PointValue} pts"));
                embed.AddFields(new EmbedFieldProperties()
                    .WithName("🏆 Reward Emojis")
                    .WithValue(rewardText)
                    .WithInline(false));
            }

            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new()
            {
                Embeds = [embed]
            }));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error listing emojis for guild {GuildId}", Context.Guild.Id);
            await RespondAsync($"❌ Error listing emojis: {ex.Message}", ephemeral: true);
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
            if (Context.Guild == null)
            {
                await RespondAsync(GetLocalizedText("errors.guild_only"), ephemeral: true);
                return;
            }

            var existingEmoji = await DbContext.Emojis
                .FirstOrDefaultAsync(e => e.ServerId == Context.Guild.Id && e.EmojiCode == emoji && e.IsActive);

            if (existingEmoji == null)
            {
                await RespondAsync(GetLocalizedText("errors.emoji_not_found"), ephemeral: true);
                return;
            }

            // Soft delete - set IsActive to false
            existingEmoji.IsActive = false;
            await DbContext.SaveChangesAsync();

            await RespondAsync($"✅ Emoji {emoji} removed successfully!");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error removing emoji for guild {GuildId} - Emoji: {Emoji}", Context.Guild.Id, emoji);
            await RespondAsync($"❌ Error removing emoji: {ex.Message}", ephemeral: true);
        }
    }

    [SlashCommand("emoji-edit", "Edit an existing emoji's configuration")]
    public async Task EditAsync(
        [SlashCommandParameter(Name = "emoji", Description = "The emoji to edit")] string emoji,
        [SlashCommandParameter(Name = "type", Description = "New emoji type (optional)")] string? type = null,
        [SlashCommandParameter(Name = "points", Description = "New points value (optional)")] int? points = null)
    {
        if (!await CheckPermissionsAsync(PermissionLevel.Config))
            return;

        try
        {
            if (Context.Guild == null)
            {
                await RespondAsync(GetLocalizedText("errors.guild_only"), ephemeral: true);
                return;
            }

            var existingEmoji = await DbContext.Emojis
                .FirstOrDefaultAsync(e => e.ServerId == Context.Guild.Id && e.EmojiCode == emoji && e.IsActive);

            if (existingEmoji == null)
            {
                await RespondAsync(GetLocalizedText("errors.emoji_not_found"), ephemeral: true);
                return;
            }

            var changes = new List<string>();

            // Update type if provided
            if (!string.IsNullOrEmpty(type))
            {
                if (!Enum.TryParse<EmojiType>(type, true, out var emojiType))
                {
                    await RespondAsync(GetLocalizedText("errors.invalid_emoji_type"), ephemeral: true);
                    return;
                }
                existingEmoji.EmojiType = emojiType;
                changes.Add($"Type: {emojiType}");
            }

            // Update points if provided
            if (points.HasValue)
            {
                if (!EmojiService.ValidatePointValue(points.Value))
                {
                    await RespondAsync(GetLocalizedText("errors.invalid_point_value"), ephemeral: true);
                    return;
                }
                existingEmoji.PointValue = points.Value;
                changes.Add($"Points: {points.Value}");
            }

            if (!changes.Any())
            {
                await RespondAsync("No changes specified. Provide at least one parameter to update.", ephemeral: true);
                return;
            }

            await DbContext.SaveChangesAsync();

            var changesText = string.Join(", ", changes);
            await RespondAsync($"✅ Emoji {emoji} updated successfully!\n**Changes**: {changesText}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error editing emoji for guild {GuildId} - Emoji: {Emoji}, Type: {Type}, Points: {Points}", 
                Context.Guild.Id, emoji, type, points);
            await RespondAsync($"❌ Error editing emoji: {ex.Message}", ephemeral: true);
        }
    }
} 