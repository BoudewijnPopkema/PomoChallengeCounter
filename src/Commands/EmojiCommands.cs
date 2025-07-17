using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Microsoft.EntityFrameworkCore;
using PomoChallengeCounter.Models;

namespace PomoChallengeCounter.Commands;

public class EmojiCommands : BaseCommand
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
            var server = await DbContext.Servers.FindAsync(Context.Guild.Id);
            if (server == null)
            {
                await RespondAsync(GetLocalizedText("errors.server_not_setup"), ephemeral: true);
                return;
            }

            // Validate emoji format
            if (!EmojiService.ValidateEmojiFormat(emoji, out var format))
            {
                await RespondAsync(GetLocalizedText("errors.invalid_emoji_format"), ephemeral: true);
                return;
            }

            // Normalize emoji to canonical format for consistent storage
            var normalizedEmoji = EmojiService.NormalizeEmoji(emoji);

            // Validate point value  
            if (!EmojiService.ValidatePointValue(points))
            {
                await RespondAsync(GetLocalizedText("errors.point_value_range"), ephemeral: true);
                return;
            }

            // Parse and validate emoji type
            if (!Enum.TryParse<EmojiType>(type, true, out var emojiType) || !Enum.IsDefined(typeof(EmojiType), emojiType))
            {
                var validTypes = string.Join(", ", Enum.GetNames<EmojiType>());
                await RespondAsync(GetLocalizedText("errors.invalid_emoji_type_message", validTypes), ephemeral: true);
                return;
            }

            // Check if challenge exists if specified
            if (challengeId.HasValue)
            {
                var challengeExists = await DbContext.Challenges
                    .AnyAsync(c => c.Id == challengeId.Value && c.ServerId == server.Id);
                if (!challengeExists)
                {
                    await RespondAsync(GetLocalizedText("errors.challenge_not_found_simple"), ephemeral: true);
                    return;
                }
            }

            // Check if emoji already exists (using normalized form for comparison)
            var existingEmoji = await DbContext.Emojis
                .Where(e => e.ServerId == server.Id && e.ChallengeId == challengeId && e.IsActive)
                .ToListAsync();
                
            var duplicateExists = existingEmoji.Any(e => EmojiService.AreEmojisEquivalent(e.EmojiCode, normalizedEmoji));
            
            if (duplicateExists)
            {
                await RespondAsync(GetLocalizedText("errors.emoji_already_configured"), ephemeral: true);
                return;
            }

            // Create new emoji record using normalized form
            var newEmoji = new Models.Emoji
            {
                ServerId = server.Id,
                ChallengeId = challengeId,
                EmojiCode = normalizedEmoji,
                EmojiType = emojiType,
                PointValue = points,
                IsActive = true
            };

            DbContext.Emojis.Add(newEmoji);
            await DbContext.SaveChangesAsync();

            // Create success embed
            var embed = new EmbedProperties()
                .WithTitle(GetLocalizedText("errors.emoji_added_success"))
                .WithColor(new Color(0x00ff00))
                .WithDescription($"Emoji has been configured for tracking!")
                .AddFields(
                    new EmbedFieldProperties().WithName("Original Input").WithValue(emoji).WithInline(true),
                    new EmbedFieldProperties().WithName("Stored As").WithValue(normalizedEmoji).WithInline(true),
                    new EmbedFieldProperties().WithName("Type").WithValue(emojiType.ToString()).WithInline(true),
                    new EmbedFieldProperties().WithName("Points").WithValue(points.ToString()).WithInline(true),
                    new EmbedFieldProperties().WithName("Format").WithValue(format.ToString()).WithInline(true)
                );

            if (challengeId.HasValue)
            {
                embed.AddFields(new EmbedFieldProperties().WithName("Challenge ID").WithValue(challengeId.Value.ToString()).WithInline(true));
            }

            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new()
            {
                Embeds = [embed]
            }));
        }
        catch (Exception ex)
        {
            await RespondAsync(GetLocalizedText("errors.error_adding_emoji", ex.Message), ephemeral: true);
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
            await RespondAsync(GetLocalizedText("errors.error_listing_emojis", ex.Message), ephemeral: true);
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

            // Find the emoji to remove (only active ones)
            var existingEmoji = await DbContext.Emojis
                .FirstOrDefaultAsync(e => e.ServerId == server.Id 
                                    && e.EmojiCode == emoji 
                                    && e.IsActive);
            
            if (existingEmoji == null)
            {
                await RespondAsync(GetLocalizedText("errors.emoji_not_found_or_removed"), ephemeral: true);
                return;
            }

            // Deactivate the emoji (soft delete)
            existingEmoji.IsActive = false;
            await DbContext.SaveChangesAsync();

            // Create success embed
            var embed = new EmbedProperties()
                .WithTitle(GetLocalizedText("errors.emoji_removed_success"))
                .WithColor(new Color(0xff9900))
                .WithDescription($"Emoji {emoji} has been removed from tracking!")
                .AddFields(
                    new EmbedFieldProperties().WithName("Type").WithValue(existingEmoji.EmojiType.ToString()).WithInline(true),
                    new EmbedFieldProperties().WithName("Points").WithValue(existingEmoji.PointValue.ToString()).WithInline(true)
                );

            if (existingEmoji.ChallengeId.HasValue)
            {
                embed.AddFields(new EmbedFieldProperties().WithName("Challenge ID").WithValue(existingEmoji.ChallengeId.Value.ToString()).WithInline(true));
            }

            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new()
            {
                Embeds = [embed]
            }));
        }
        catch (Exception ex)
        {
            await RespondAsync(GetLocalizedText("errors.error_removing_emoji", ex.Message), ephemeral: true);
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
            var server = await DbContext.Servers.FindAsync(Context.Guild.Id);
            if (server == null)
            {
                await RespondAsync(GetLocalizedText("errors.server_not_setup"), ephemeral: true);
                return;
            }

            // Find the emoji to edit (only active ones)
            var existingEmoji = await DbContext.Emojis
                .FirstOrDefaultAsync(e => e.ServerId == server.Id 
                                    && e.EmojiCode == emoji 
                                    && e.IsActive);
            
            if (existingEmoji == null)
            {
                await RespondAsync(GetLocalizedText("errors.emoji_not_found_simple"), ephemeral: true);
                return;
            }

            bool hasChanges = false;
            var originalType = existingEmoji.EmojiType;
            var originalPoints = existingEmoji.PointValue;

            // Update emoji type if provided
            if (!string.IsNullOrWhiteSpace(type))
            {
                if (!Enum.TryParse<EmojiType>(type, true, out var emojiType) || !Enum.IsDefined(typeof(EmojiType), emojiType))
                {
                    var validTypes = string.Join(", ", Enum.GetNames<EmojiType>());
                    await RespondAsync(GetLocalizedText("errors.invalid_emoji_type_message", validTypes), ephemeral: true);
                    return;
                }

                if (existingEmoji.EmojiType != emojiType)
                {
                    existingEmoji.EmojiType = emojiType;
                    hasChanges = true;
                }
            }

            // Update point value if provided
            if (points.HasValue)
            {
                if (!EmojiService.ValidatePointValue(points.Value))
                {
                    await RespondAsync(GetLocalizedText("errors.point_value_range"), ephemeral: true);
                    return;
                }

                if (existingEmoji.PointValue != points.Value)
                {
                    existingEmoji.PointValue = points.Value;
                    hasChanges = true;
                }
            }

            if (!hasChanges)
            {
                await RespondAsync(GetLocalizedText("errors.no_changes_specified_message"), ephemeral: true);
                return;
            }

            await DbContext.SaveChangesAsync();

            // Create success embed
            var embed = new EmbedProperties()
                .WithTitle(GetLocalizedText("errors.emoji_updated_success"))
                .WithColor(new Color(0x0099ff))
                .WithDescription($"Emoji {emoji} has been updated!");

            // Show changes made
            if (originalType != existingEmoji.EmojiType)
            {
                embed.AddFields(new EmbedFieldProperties()
                    .WithName("Type Changed")
                    .WithValue($"{originalType} → {existingEmoji.EmojiType}")
                    .WithInline(true));
            }

            if (originalPoints != existingEmoji.PointValue)
            {
                embed.AddFields(new EmbedFieldProperties()
                    .WithName("Points Changed")
                    .WithValue($"{originalPoints} → {existingEmoji.PointValue}")
                    .WithInline(true));
            }

            if (existingEmoji.ChallengeId.HasValue)
            {
                embed.AddFields(new EmbedFieldProperties().WithName("Challenge ID").WithValue(existingEmoji.ChallengeId.Value.ToString()).WithInline(true));
            }

            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new()
            {
                Embeds = [embed]
            }));
        }
        catch (Exception ex)
        {
            await RespondAsync(GetLocalizedText("errors.error_editing_emoji", ex.Message), ephemeral: true);
        }
    }
} 