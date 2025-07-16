using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Microsoft.EntityFrameworkCore;
using PomoChallengeCounter.Services;

namespace PomoChallengeCounter.Commands;

public class AdminCommands : BaseCommand
{
    public MessageProcessorService MessageProcessor { get; set; } = null!;

    [SlashCommand("stats", "View server statistics")]
    public async Task StatsAsync()
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

            // Get basic stats
            var challengeCount = await DbContext.Challenges
                .CountAsync(c => c.ServerId == server.Id);

            var activeChallengeCount = await DbContext.Challenges
                .CountAsync(c => c.ServerId == server.Id && c.IsActive);

            var totalWeeks = await DbContext.Weeks
                .Include(w => w.Challenge)
                .CountAsync(w => w.Challenge.ServerId == server.Id);

            var totalMessages = await DbContext.MessageLogs
                .Include(ml => ml.Week)
                .ThenInclude(w => w.Challenge)
                .CountAsync(ml => ml.Week.Challenge.ServerId == server.Id);

            var totalPoints = await DbContext.MessageLogs
                .Include(ml => ml.Week)
                .ThenInclude(w => w.Challenge)
                .Where(ml => ml.Week.Challenge.ServerId == server.Id)
                .SumAsync(ml => ml.PomodoroPoints + ml.BonusPoints);

            var emojiCount = await DbContext.Emojis
                .CountAsync(e => e.ServerId == server.Id && e.IsActive);

            var embed = new EmbedProperties()
                .WithTitle($"📈 Server Statistics - {Context.Guild.Name}")
                .WithColor(new Color(0x00ff00))
                .WithTimestamp(DateTimeOffset.UtcNow)
                .AddFields(
                    new EmbedFieldProperties()
                        .WithName("Total Challenges")
                        .WithValue(challengeCount.ToString())
                        .WithInline(true),
                    new EmbedFieldProperties()
                        .WithName("Active Challenges")
                        .WithValue(activeChallengeCount.ToString())
                        .WithInline(true),
                    new EmbedFieldProperties()
                        .WithName("Total Weeks")
                        .WithValue(totalWeeks.ToString())
                        .WithInline(true),
                    new EmbedFieldProperties()
                        .WithName("Total Messages")
                        .WithValue(totalMessages.ToString())
                        .WithInline(true),
                    new EmbedFieldProperties()
                        .WithName("Total Points")
                        .WithValue(totalPoints.ToString())
                        .WithInline(true),
                    new EmbedFieldProperties()
                        .WithName("Active Emojis")
                        .WithValue(emojiCount.ToString())
                        .WithInline(true)
                );

            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new()
            {
                Embeds = [embed]
            }));
        }
        catch (Exception ex)
        {
            await RespondAsync($"Error retrieving stats: {ex.Message}", ephemeral: true);
        }
    }

    [SlashCommand("debug", "View debug information")]
    public async Task DebugAsync()
    {
        try
        {
            var server = await DbContext.Servers.FindAsync(Context.Guild.Id);
            
            var embed = new EmbedProperties()
                .WithTitle("🔧 Debug Information")
                .WithColor(new Color(0xffa500));

            if (server == null)
            {
                embed.AddFields(new EmbedFieldProperties()
                    .WithName("❌ Server Configuration")
                    .WithValue("Not configured - run /setup")
                    .WithInline(false));
            }
            else
            {
                embed.AddFields(
                    new EmbedFieldProperties()
                        .WithName("✅ Server Configuration")
                        .WithValue($"Language: {server.Language}")
                        .WithInline(true),
                    new EmbedFieldProperties()
                        .WithName("Category ID")
                        .WithValue(server.CategoryId?.ToString() ?? "❌ Not set")
                        .WithInline(true),
                    new EmbedFieldProperties()
                        .WithName("Config Role ID")
                        .WithValue(server.ConfigRoleId?.ToString() ?? "❌ Not set")
                        .WithInline(true),
                    new EmbedFieldProperties()
                        .WithName("Ping Role ID")
                        .WithValue(server.PingRoleId?.ToString() ?? "❌ Not set")
                        .WithInline(true)
                );

                // Current challenge info
                var currentChallenge = await DbContext.Challenges
                    .Where(c => c.ServerId == server.Id && c.IsActive)
                    .FirstOrDefaultAsync();

                if (currentChallenge != null)
                {
                    embed.AddFields(
                        new EmbedFieldProperties()
                            .WithName("Current Challenge")
                            .WithValue($"Q{currentChallenge.SemesterNumber}: {currentChallenge.Theme}")
                            .WithInline(false),
                        new EmbedFieldProperties()
                            .WithName("Challenge Status")
                            .WithValue($"Started: {currentChallenge.IsStarted} | Active: {currentChallenge.IsActive}")
                            .WithInline(true)
                    );
                }
                else
                {
                    embed.AddFields(new EmbedFieldProperties()
                        .WithName("Current Challenge")
                        .WithValue("❌ None set")
                        .WithInline(false));
                }
            }

            embed.WithTimestamp(DateTimeOffset.UtcNow);

            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new()
            {
                Embeds = [embed]
            }));
        }
        catch (Exception ex)
        {
            await RespondAsync($"Error retrieving debug info: {ex.Message}", ephemeral: true);
        }
    }

    [SlashCommand("help", "Get help information")]
    public async Task HelpAsync(
        [SlashCommandParameter(Name = "command", Description = "Specific command help (optional)")] string? command = null)
    {
        var embed = new EmbedProperties()
            .WithTitle("🤖 PomoChallengeCounter Help")
            .WithColor(new Color(0x00ff00));

        if (string.IsNullOrEmpty(command))
        {
            // General help
            embed.WithDescription("Automated Discord bot for tracking pomodoro challenges in university servers.")
                .AddFields(
                    new EmbedFieldProperties()
                        .WithName("🛠️ Setup Commands")
                        .WithValue(
                            "`/setup` - Initial bot configuration\n" +
                            "`/config category` - Set challenge category\n" +
                            "`/config language` - Set server language\n" +
                            "`/config roles` - Configure permission roles")
                        .WithInline(false),
                    new EmbedFieldProperties()
                        .WithName("🎯 Challenge Management")
                        .WithValue(
                            "`/challenge create` - Create new challenge\n" +
                            "`/challenge start` - Start challenge\n" +
                            "`/challenge stop` - Stop challenge\n" +
                            "`/challenge info` - View challenge details\n" +
                            "`/challenge list` - List all challenges\n" +
                            "`/challenge deactivate` - Deactivate challenge")
                        .WithInline(false),
                    new EmbedFieldProperties()
                        .WithName("😀 Emoji System")
                        .WithValue(
                            "`/emoji add` - Add new emoji\n" +
                            "`/emoji remove` - Remove emoji\n" +
                            "`/emoji list` - List configured emojis\n" +
                            "`/emoji edit` - Edit emoji settings")
                        .WithInline(false),
                    new EmbedFieldProperties()
                        .WithName("📊 Thread & Leaderboard")
                        .WithValue(
                            "`/thread-create` - Create week thread\n" +
                            "`/thread-ping` - Ping configured role\n" +
                            "`/leaderboard <week>` - Generate leaderboard for specific week")
                        .WithInline(false),
                    new EmbedFieldProperties()
                        .WithName("⚙️ Admin Tools")
                        .WithValue(
                            "`/stats` - Server statistics\n" +
                            "`/debug` - Debug information\n" +
                            "`/help [command]` - Command help")
                        .WithInline(false),
                    new EmbedFieldProperties()
                        .WithName("Footer")
                        .WithValue("Use /help [command] for detailed information about a specific command")
                        .WithInline(false));
        }
        else
        {
            // Specific command help
            embed.WithDescription($"Help for command: `{command}`")
                .AddFields(new EmbedFieldProperties()
                    .WithName("Details")
                    .WithValue("For detailed command information, please refer to the documentation or contact an administrator.")
                    .WithInline(false));
        }

        await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new()
        {
            Embeds = [embed]
        }));
    }

    [SlashCommand("leaderboard", "Generate a leaderboard for a specific week")]
    public async Task GenerateLeaderboardAsync(
        [SlashCommandParameter(Name = "week", Description = "Week number to generate leaderboard for")] int weekNumber)
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

            // Get the current active challenge
            var currentChallenge = await DbContext.Challenges
                .Where(c => c.ServerId == server.Id && c.IsActive)
                .FirstOrDefaultAsync();

            if (currentChallenge == null)
            {
                await RespondAsync("No active challenge found. Please start a challenge first.", ephemeral: true);
                return;
            }

            // Find the specific week
            var week = await DbContext.Weeks
                .Where(w => w.ChallengeId == currentChallenge.Id && w.WeekNumber == weekNumber)
                .FirstOrDefaultAsync();

            if (week == null)
            {
                await RespondAsync($"Week {weekNumber} not found in the current challenge (Semester {currentChallenge.SemesterNumber}).", ephemeral: true);
                return;
            }

            // Generate the leaderboard embed
            var embed = await MessageProcessor.GenerateLeaderboardEmbedAsync(week.Id);

            // Send the response
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
} 