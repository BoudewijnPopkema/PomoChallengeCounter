using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PomoChallengeCounter.Data;
using PomoChallengeCounter.Services;
using PomoChallengeCounter.Models;

namespace PomoChallengeCounter.Commands;

public class ChallengeCommands(
    ILocalizationService localizationService, 
    PomoChallengeDbContext dbContext, 
    IEmojiService emojiService, 
    IChallengeService challengeService,
    ILogger<ChallengeCommands> logger) : BaseCommand<ChallengeCommands>(localizationService, dbContext, emojiService, logger)
{
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
        [SlashCommandParameter(Name = "end_date", Description = "End date (YYYY-MM-DD)")] string endDate)
    {
        if (!await CheckPermissionsAsync(PermissionLevel.Config))
            return;

        try
        {
            // Check if server is set up
            var server = await DbContext.Servers.FindAsync(Context.Guild.Id);
            if (server == null)
            {
                await RespondWithErrorAsync("errors.server_not_setup");
                return;
            }

            // Parse dates
            if (!DateOnly.TryParseExact(startDate, "yyyy-MM-dd", out var parsedStartDate))
            {
                await RespondAsync("❌ Invalid start date format. Please use YYYY-MM-DD (e.g., 2024-01-08)", ephemeral: true);
                return;
            }

            if (!DateOnly.TryParseExact(endDate, "yyyy-MM-dd", out var parsedEndDate))
            {
                await RespondAsync("❌ Invalid end date format. Please use YYYY-MM-DD (e.g., 2024-03-31)", ephemeral: true);
                return;
            }

            // Create challenge using service (weeks calculated automatically)
            var result = await challengeService.CreateChallengeAsync(
                Context.Guild.Id, 
                semester, 
                theme, 
                parsedStartDate, 
                parsedEndDate);

            if (result.IsSuccess && result.Challenge != null)
            {
                var embed = new EmbedProperties()
                    .WithTitle("✅ Challenge Created Successfully")
                    .WithColor(new Color(0x00ff00))
                    .AddFields(
                        new EmbedFieldProperties()
                            .WithName("Semester")
                            .WithValue($"Q{result.Challenge.SemesterNumber}")
                            .WithInline(true),
                        new EmbedFieldProperties()
                            .WithName("Theme")
                            .WithValue(result.Challenge.Theme)
                            .WithInline(true),
                        new EmbedFieldProperties()
                            .WithName("Duration")
                            .WithValue($"{result.Challenge.WeekCount} weeks")
                            .WithInline(true),
                        new EmbedFieldProperties()
                            .WithName("Start Date")
                            .WithValue(result.Challenge.StartDate.ToString("yyyy-MM-dd"))
                            .WithInline(true),
                        new EmbedFieldProperties()
                            .WithName("End Date")
                            .WithValue(result.Challenge.EndDate.ToString("yyyy-MM-dd"))
                            .WithInline(true),
                        new EmbedFieldProperties()
                            .WithName("Next Steps")
                            .WithValue("Use `/challenge start` to begin the challenge and create initial threads")
                            .WithInline(false)
                    );

                await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new()
                {
                    Embeds = [embed]
                }));
            }
            else
            {
                await RespondAsync($"❌ Failed to create challenge: {result.ErrorMessage}", ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error creating challenge for guild {GuildId} - Semester: {Semester}, Theme: {Theme}, StartDate: {StartDate}, EndDate: {EndDate}", 
                Context.Guild.Id, semester, theme, startDate, endDate);
            await RespondAsync($"❌ Error creating challenge: {ex.Message}", ephemeral: true);
        }
    }

    [SlashCommand("challenge-start", "Start an existing challenge")]
    public async Task StartAsync(
        [SlashCommandParameter(Name = "challenge_id", Description = "Challenge ID (optional - uses current if not specified)")] int? challengeId = null)
    {
        if (!await CheckPermissionsAsync(PermissionLevel.Config))
            return;

        try
        {
            var server = await DbContext.Servers.FindAsync(Context.Guild.Id);
            if (server == null)
            {
                await RespondWithErrorAsync("errors.server_not_setup");
                return;
            }

            Challenge? challengeToStart = null;

            // If no challenge ID provided, use the most recent inactive challenge
            if (challengeId == null)
            {
                challengeToStart = await DbContext.Challenges
                    .Where(c => c.ServerId == server.Id && !c.IsStarted)
                    .OrderByDescending(c => c.Id)
                    .FirstOrDefaultAsync();

                if (challengeToStart == null)
                {
                    await RespondAsync("❌ No inactive challenges found. Create a challenge first with `/challenge create`", ephemeral: true);
                    return;
                }
            }
            else
            {
                challengeToStart = await DbContext.Challenges
                    .FirstOrDefaultAsync(c => c.Id == challengeId.Value && c.ServerId == server.Id);

                if (challengeToStart == null)
                {
                    await RespondAsync($"❌ Challenge with ID {challengeId.Value} not found", ephemeral: true);
                    return;
                }
            }

            // Start the challenge
            var result = await challengeService.StartChallengeAsync(challengeToStart.Id);

            if (result.IsSuccess && result.Challenge != null)
            {
                var embed = new EmbedProperties()
                    .WithTitle("🚀 Challenge Started!")
                    .WithColor(new Color(0x00ff00))
                    .AddFields(
                        new EmbedFieldProperties()
                            .WithName("Challenge")
                            .WithValue($"Q{result.Challenge.SemesterNumber}: {result.Challenge.Theme}")
                            .WithInline(false),
                        new EmbedFieldProperties()
                            .WithName("Duration")
                            .WithValue($"{result.Challenge.WeekCount} weeks ({result.Challenge.StartDate:yyyy-MM-dd} to {result.Challenge.EndDate:yyyy-MM-dd})")
                            .WithInline(false),
                        new EmbedFieldProperties()
                            .WithName("Status")
                            .WithValue("✅ Challenge is now active and ready for participation!")
                            .WithInline(false),
                        new EmbedFieldProperties()
                            .WithName("Next Steps")
                            .WithValue("Weekly threads will be automatically created every Monday at 9 AM (Amsterdam time)\nLeaderboards will be posted every Tuesday at 12 PM")
                            .WithInline(false)
                    );

                await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new()
                {
                    Embeds = [embed]
                }));
            }
            else
            {
                await RespondAsync($"❌ Failed to start challenge: {result.ErrorMessage}", ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error starting challenge for guild {GuildId}, ChallengeId: {ChallengeId}", 
                Context.Guild.Id, challengeId);
            await RespondAsync($"❌ Error starting challenge: {ex.Message}", ephemeral: true);
        }
    }

    [SlashCommand("challenge-stop", "Stop the current challenge")]
    public async Task StopAsync(
        [SlashCommandParameter(Name = "challenge_id", Description = "Challenge ID (optional - uses current if not specified)")] int? challengeId = null)
    {
        if (!await CheckPermissionsAsync(PermissionLevel.Config))
            return;

        try
        {
            var server = await DbContext.Servers.FindAsync(Context.Guild.Id);
            if (server == null)
            {
                await RespondWithErrorAsync("errors.server_not_setup");
                return;
            }

            Challenge? challengeToStop = null;

            // If no challenge ID provided, use the current active challenge
            if (challengeId == null)
            {
                challengeToStop = await DbContext.Challenges
                    .Where(c => c.ServerId == server.Id && c.IsActive && c.IsCurrent)
                    .FirstOrDefaultAsync();

                if (challengeToStop == null)
                {
                    await RespondAsync("❌ No active challenge found to stop", ephemeral: true);
                    return;
                }
            }
            else
            {
                challengeToStop = await DbContext.Challenges
                    .FirstOrDefaultAsync(c => c.Id == challengeId.Value && c.ServerId == server.Id);

                if (challengeToStop == null)
                {
                    await RespondAsync($"❌ Challenge with ID {challengeId.Value} not found", ephemeral: true);
                    return;
                }
            }

            // Stop the challenge
            var result = await challengeService.StopChallengeAsync(challengeToStop.Id);

            if (result.IsSuccess)
            {
                var embed = new EmbedProperties()
                    .WithTitle("⏹️ Challenge Stopped")
                    .WithColor(new Color(0xff9900))
                    .AddFields(
                        new EmbedFieldProperties()
                            .WithName("Challenge")
                            .WithValue($"Q{challengeToStop.SemesterNumber}: {challengeToStop.Theme}")
                            .WithInline(false),
                        new EmbedFieldProperties()
                            .WithName("Status")
                            .WithValue("Challenge has been stopped and is no longer active")
                            .WithInline(false)
                    );

                await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new()
                {
                    Embeds = [embed]
                }));
            }
            else
            {
                await RespondAsync($"❌ Failed to stop challenge: {result.ErrorMessage}", ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error stopping challenge for guild {GuildId}, ChallengeId: {ChallengeId}", 
                Context.Guild.Id, challengeId);
            await RespondAsync($"❌ Error stopping challenge: {ex.Message}", ephemeral: true);
        }
    }

    [SlashCommand("challenge-deactivate", "Deactivate challenge without deleting Discord content")]
    public async Task DeactivateAsync(
        [SlashCommandParameter(Name = "challenge_id", Description = "Challenge ID (optional - uses current if not specified)")] int? challengeId = null)
    {
        if (!await CheckPermissionsAsync(PermissionLevel.Config))
            return;

        try
        {
            var server = await DbContext.Servers.FindAsync(Context.Guild.Id);
            if (server == null)
            {
                await RespondWithErrorAsync("errors.server_not_setup");
                return;
            }

            Challenge? challengeToDeactivate = null;

            // If no challenge ID provided, use the current active challenge
            if (challengeId == null)
            {
                challengeToDeactivate = await DbContext.Challenges
                    .Where(c => c.ServerId == server.Id && c.IsActive)
                    .FirstOrDefaultAsync();

                if (challengeToDeactivate == null)
                {
                    await RespondAsync("❌ No active challenge found to deactivate", ephemeral: true);
                    return;
                }
            }
            else
            {
                challengeToDeactivate = await DbContext.Challenges
                    .FirstOrDefaultAsync(c => c.Id == challengeId.Value && c.ServerId == server.Id);

                if (challengeToDeactivate == null)
                {
                    await RespondAsync($"❌ Challenge with ID {challengeId.Value} not found", ephemeral: true);
                    return;
                }
            }

            // Deactivate the challenge
            var result = await challengeService.DeactivateChallengeAsync(challengeToDeactivate.Id);

            if (result.IsSuccess)
            {
                var embed = new EmbedProperties()
                    .WithTitle("💤 Challenge Deactivated")
                    .WithColor(new Color(0x808080))
                    .AddFields(
                        new EmbedFieldProperties()
                            .WithName("Challenge")
                            .WithValue($"Q{challengeToDeactivate.SemesterNumber}: {challengeToDeactivate.Theme}")
                            .WithInline(false),
                        new EmbedFieldProperties()
                            .WithName("Status")
                            .WithValue("Challenge deactivated - message processing stopped")
                            .WithInline(false),
                        new EmbedFieldProperties()
                            .WithName("Discord Content")
                            .WithValue("All Discord channels, threads, and messages remain intact")
                            .WithInline(false)
                    );

                await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new()
                {
                    Embeds = [embed]
                }));
            }
            else
            {
                await RespondAsync($"❌ Failed to deactivate challenge: {result.ErrorMessage}", ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deactivating challenge for guild {GuildId}, ChallengeId: {ChallengeId}", 
                Context.Guild.Id, challengeId);
            await RespondAsync($"❌ Error deactivating challenge: {ex.Message}", ephemeral: true);
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
            Logger.LogError(ex, "Error listing challenges for guild {GuildId}", Context.Guild.Id);
            await RespondAsync($"Error listing challenges: {ex.Message}", ephemeral: true);
        }
    }

    [SlashCommand("challenge-info", "Get information about the current challenge")]
    public async Task InfoAsync()
    {
        if (!await CheckPermissionsAsync(PermissionLevel.Config))
            return;

        try
        {
            var challenge = await DbContext.Challenges
                .Include(c => c.Server)
                .Where(c => c.ServerId == Context.Guild.Id && c.IsCurrent)
                .FirstOrDefaultAsync();

            if (challenge == null)
            {
                await RespondAsync("❌ No current challenge found for this server.", ephemeral: true);
                return;
            }

            var embed = new EmbedProperties()
                .WithTitle($"📊 Challenge Information")
                .WithColor(new Color(0x3498db))
                .AddFields(
                    new EmbedFieldProperties()
                        .WithName("Semester")
                        .WithValue($"Q{challenge.SemesterNumber}")
                        .WithInline(true),
                    new EmbedFieldProperties()
                        .WithName("Theme")
                        .WithValue(challenge.Theme)
                        .WithInline(true),
                    new EmbedFieldProperties()
                        .WithName("Status")
                        .WithValue(challenge.IsActive ? "🟢 Active" : "🔴 Inactive")
                        .WithInline(true),
                    new EmbedFieldProperties()
                        .WithName("Duration")
                        .WithValue($"{challenge.WeekCount} weeks")
                        .WithInline(true),
                    new EmbedFieldProperties()
                        .WithName("Start Date")
                        .WithValue(challenge.StartDate.ToString("yyyy-MM-dd"))
                        .WithInline(true),
                    new EmbedFieldProperties()
                        .WithName("End Date")
                        .WithValue(challenge.EndDate.ToString("yyyy-MM-dd"))
                        .WithInline(true)
                );

            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new()
            {
                Embeds = [embed]
            }));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error retrieving challenge info for guild {GuildId}", Context.Guild.Id);
            await RespondAsync($"❌ Error retrieving challenge info: {ex.Message}", ephemeral: true);
        }
    }

    [SlashCommand("challenge-import", "Import existing challenge from Discord channel")]
    public async Task ImportAsync(
        [SlashCommandParameter(Name = "channel", Description = "Discord channel containing the challenge")] Channel channel,
        [SlashCommandParameter(Name = "semester", Description = "Semester number (1-5)")] int semester,
        [SlashCommandParameter(Name = "theme", Description = "Challenge theme name")] string theme)
    {
        if (!await CheckPermissionsAsync(PermissionLevel.Config))
            return;

        try
        {
            // Check if server is set up
            var server = await DbContext.Servers.FindAsync(Context.Guild.Id);
            if (server == null)
            {
                await RespondWithErrorAsync("errors.server_not_setup");
                return;
            }

            // Validate semester number
            if (semester < 1 || semester > 5)
            {
                await RespondAsync("❌ Semester number must be between 1 and 5.", ephemeral: true);
                return;
            }

            // Start the import process
            var importResult = await challengeService.ImportChallengeAsync(
                Context.Guild.Id,
                channel.Id,
                semester,
                theme);

            if (importResult.IsSuccess && importResult.Challenge != null)
            {
                var successMessage = $"✅ Challenge import completed!\n" +
                    $"**Semester:** Q{importResult.Challenge.SemesterNumber}\n" +
                    $"**Theme:** {importResult.Challenge.Theme}\n" +
                    $"**Threads Found:** {importResult.ThreadsProcessed}\n" +
                    $"**Messages Processed:** {importResult.MessagesProcessed}\n" +
                    $"**Users Found:** {importResult.UsersFound}\n\n" +
                    $"Use `/challenge start` to activate the challenge for ongoing automation.";

                if (importResult.Warnings.Any())
                {
                    successMessage += $"\n\n⚠️ **Warnings:**\n{string.Join("\n", importResult.Warnings)}";
                }

                await RespondAsync(successMessage, ephemeral: true);
            }
            else
            {
                await RespondAsync($"❌ Import failed: {importResult.ErrorMessage}", ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error importing challenge for guild {GuildId} - ChannelId: {ChannelId}, Semester: {Semester}, Theme: {Theme}", 
                Context.Guild.Id, channel.Id, semester, theme);
            await RespondAsync($"❌ Import error: {ex.Message}", ephemeral: true);
        }
    }
} 