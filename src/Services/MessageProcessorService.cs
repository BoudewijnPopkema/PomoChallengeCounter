using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using PomoChallengeCounter.Data;
using PomoChallengeCounter.Models;
using PomoChallengeCounter.Models.Results;

namespace PomoChallengeCounter.Services;

public class MessageProcessorService(
    PomoChallengeDbContext context,
    IEmojiService emojiService,
    IServiceProvider serviceProvider,
    ILogger<MessageProcessorService> logger)
{
    public async Task<MessageProcessingResult> ProcessMessageAsync(ulong messageId, ulong userId, string messageContent, ulong? channelId = null, bool forceReprocess = false)
    {
        try
        {
            // Check if message was already processed
            var existingLog = await context.MessageLogs
                .Include(ml => ml.Week)
                .ThenInclude(w => w.Challenge)
                .FirstOrDefaultAsync(ml => ml.MessageId == messageId);
                
            if (existingLog != null && !forceReprocess)
            {
                logger.LogDebug("Message {MessageId} already processed, skipping", messageId);
                return new MessageProcessingResult { IsSuccess = false, Reason = "Already processed" };
            }

            // Find the appropriate week for this message (use existing if reprocessing)
            var week = existingLog?.Week ?? await FindWeekForMessageAsync(channelId);
            if (week == null)
            {
                logger.LogDebug("No active week found for message {MessageId}", messageId);
                return new MessageProcessingResult { IsSuccess = false, Reason = "No active week" };
            }

            return await ProcessMessageForWeekAsync(messageId, userId, messageContent, week, forceReprocess);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process message {MessageId}", messageId);
            return new MessageProcessingResult { IsSuccess = false, Reason = "Processing error" };
        }
    }

    private async Task<MessageProcessingResult> ProcessMessageForWeekAsync(ulong messageId, ulong userId, string messageContent, Week week, bool forceReprocess = false)
    {
        try
        {
            MessageLog? existingLog;
            if (!forceReprocess)
            {
                existingLog = await context.MessageLogs
                    .FirstOrDefaultAsync(ml => ml.MessageId == messageId);
                    
                if (existingLog != null)
                {
                    logger.LogDebug("Message {MessageId} already processed, skipping", messageId);
                    return new MessageProcessingResult { IsSuccess = false, Reason = "Already processed" };
                }
            }
            else
            {
                existingLog = await context.MessageLogs
                    .FirstOrDefaultAsync(ml => ml.MessageId == messageId);
            }
            
            var detectionResult = emojiService.DetectEmojis(messageContent);
            if (detectionResult.TotalCount == 0)
            {
                logger.LogDebug("No emojis detected in message {MessageId}", messageId);
                return new MessageProcessingResult { IsSuccess = false, Reason = "No emojis" };
            }
            
            var pointsResult = await CalculatePointsAsync(detectionResult, week.Challenge.ServerId, week.ChallengeId);

            MessageLog messageLog;
            if (existingLog != null)
            {
                existingLog.PomodoroPoints = pointsResult.PomodoroPoints;
                existingLog.BonusPoints = pointsResult.BonusPoints;
                existingLog.GoalPoints = pointsResult.GoalPoints;
                messageLog = existingLog;
                
                logger.LogDebug("Reprocessed message {MessageId}", messageId);
            }
            else
            {
                messageLog = new MessageLog
                {
                    MessageId = messageId,
                    UserId = userId,
                    WeekId = week.Id,
                    PomodoroPoints = pointsResult.PomodoroPoints,
                    BonusPoints = pointsResult.BonusPoints,
                    GoalPoints = pointsResult.GoalPoints
                };

                context.MessageLogs.Add(messageLog);
            }

            await context.SaveChangesAsync();

            var action = existingLog != null ? "Reprocessed" : "Processed";
            logger.LogInformation("{Action} message {MessageId} for user {UserId}: {Pomodoro} pomodoro, {Bonus} bonus, {Goal} goal points",
                action, messageId, userId, pointsResult.PomodoroPoints, pointsResult.BonusPoints, pointsResult.GoalPoints);

            return new MessageProcessingResult 
            { 
                IsSuccess = true, 
                MessageLog = messageLog,
                DetectedEmojis = detectionResult.TotalCount
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process message {MessageId} for week {WeekId}", messageId, week.Id);
            return new MessageProcessingResult { IsSuccess = false, Reason = "Processing error" };
        }
    }

    public async Task<bool> UpdateMessageAsync(ulong messageId, string newContent)
    {
        try
        {
            var existingLog = await context.MessageLogs
                .Include(ml => ml.Week)
                .ThenInclude(w => w.Challenge)
                .FirstOrDefaultAsync(ml => ml.MessageId == messageId);

            if (existingLog == null)
            {
                logger.LogDebug("Message {MessageId} not found for update", messageId);
                return false;
            }

            // Check if challenge is still active
            if (!existingLog.Week.Challenge.IsActive)
            {
                logger.LogDebug("Challenge inactive, ignoring message {MessageId} update", messageId);
                return false;
            }

            // Recalculate points
            var detectionResult = emojiService.DetectEmojis(newContent);
            var pointsResult = await CalculatePointsAsync(detectionResult, existingLog.Week.Challenge.ServerId, existingLog.Week.ChallengeId);

            // Update existing log
            existingLog.PomodoroPoints = pointsResult.PomodoroPoints;
            existingLog.BonusPoints = pointsResult.BonusPoints;
            existingLog.GoalPoints = pointsResult.GoalPoints;

            await context.SaveChangesAsync();

            logger.LogInformation("Updated message {MessageId}: {Pomodoro} pomodoro, {Bonus} bonus, {Goal} goal points",
                messageId, pointsResult.PomodoroPoints, pointsResult.BonusPoints, pointsResult.GoalPoints);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update message {MessageId}", messageId);
            return false;
        }
    }

    public async Task<bool> DeleteMessageAsync(ulong messageId)
    {
        try
        {
            var existingLog = await context.MessageLogs
                .FirstOrDefaultAsync(ml => ml.MessageId == messageId);

            if (existingLog == null)
            {
                logger.LogDebug("Message {MessageId} not found for deletion", messageId);
                return false;
            }

            context.MessageLogs.Remove(existingLog);
            await context.SaveChangesAsync();

            logger.LogInformation("Deleted message log for message {MessageId}", messageId);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete message {MessageId}", messageId);
            return false;
        }
    }

    private async Task<Week?> FindWeekForMessageAsync(ulong? channelId)
    {
        // Only process messages from specific challenge threads - no fallback
        if (!channelId.HasValue)
        {
            logger.LogDebug("No channel ID provided, cannot determine week");
            return null;
        }

        var weekByThread = await context.Weeks
            .Include(w => w.Challenge)
            .Where(w => (w.ThreadId == channelId.Value || w.GoalThreadId == channelId.Value) 
                       && w.Challenge.IsActive)
            .FirstOrDefaultAsync();
            
        if (weekByThread != null)
        {
            logger.LogDebug("Found week {WeekNumber} for challenge thread {ChannelId}", 
                weekByThread.WeekNumber, channelId.Value);
            return weekByThread;
        }

        logger.LogDebug("Channel {ChannelId} is not a challenge thread, ignoring message", channelId.Value);
        return null;
    }

    private async Task<PointsCalculationResult> CalculatePointsAsync(EmojiDetectionResult detectionResult, ulong serverId, int challengeId)
    {
        var result = new PointsCalculationResult();

        // Get all configured emojis for this server/challenge
        var emojis = await context.Emojis
            .Where(e => e.ServerId == serverId && e.IsActive)
            .Where(e => e.ChallengeId == null || e.ChallengeId == challengeId)
            .ToListAsync();

        // Count points for each detected emoji
        foreach (var detectedEmoji in detectionResult.AllEmojis)
        {
            var matchingEmoji = emojis.FirstOrDefault(e => 
                e.EmojiCode == detectedEmoji || 
                emojiService.AreEmojisEquivalent(e.EmojiCode, detectedEmoji));
                
            if (matchingEmoji == null) continue;

            switch (matchingEmoji.EmojiType)
            {
                case EmojiType.Pomodoro:
                    result.PomodoroPoints += matchingEmoji.PointValue;
                    break;
                case EmojiType.Bonus:
                    result.BonusPoints += matchingEmoji.PointValue;
                    break;
                case EmojiType.Goal:
                    result.GoalPoints += matchingEmoji.PointValue;
                    break;
                case EmojiType.Reward:
                    // Reward emojis don't contribute to points
                    break;
                default:
                    logger.LogWarning("Unknown emoji type {EmojiType} for emoji {EmojiCode}", matchingEmoji.EmojiType, matchingEmoji.EmojiCode);
                    break;
            }
        }

        return result;
    }

    public async Task<WeekRescanResult> RescanWeekAsync(int weekId, List<(ulong MessageId, ulong UserId, string Content)> messages)
    {
        try
        {
            var result = new WeekRescanResult();
            
            logger.LogInformation("Starting rescan of week {WeekId} with {MessageCount} messages", weekId, messages.Count);
            
            // Get all existing message IDs for this week
            var existingMessageIds = await context.MessageLogs
                .Where(ml => ml.WeekId == weekId)
                .Select(ml => ml.MessageId)
                .ToListAsync();
            
            var currentMessageIds = messages.Select(m => m.MessageId).ToList();
            
            // Find messages that no longer exist and should be deleted
            var messagesToDelete = existingMessageIds.Except(currentMessageIds).ToList();
            
            // Delete obsolete message logs
            if (messagesToDelete.Count > 0)
            {
                var logsToDelete = await context.MessageLogs
                    .Where(ml => messagesToDelete.Contains(ml.MessageId))
                    .ToListAsync();
                
                context.MessageLogs.RemoveRange(logsToDelete);
                result.DeletedCount = logsToDelete.Count;
                
                logger.LogInformation("Deleted {Count} obsolete message logs", result.DeletedCount);
            }
            
            // Get the week for direct processing (bypass channel detection)
            var week = await context.Weeks
                .Include(w => w.Challenge)
                .FirstOrDefaultAsync(w => w.Id == weekId);
                
            if (week == null)
            {
                logger.LogError("Week {WeekId} not found for rescan", weekId);
                return result;
            }

            // Process all current messages
            foreach (var (messageId, userId, content) in messages)
            {
                var processResult = await ProcessMessageForWeekAsync(messageId, userId, content, week, forceReprocess: true);
                
                if (processResult.IsSuccess)
                {
                    result.ProcessedCount++;
                }
                else
                {
                    result.SkippedCount++;
                    logger.LogDebug("Skipped message {MessageId} during rescan: {Reason}", messageId, processResult.Reason);
                }
            }
            
            logger.LogInformation("Completed week {WeekId} rescan: {Processed} processed, {Skipped} skipped, {Deleted} deleted", 
                weekId, result.ProcessedCount, result.SkippedCount, result.DeletedCount);
            
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to rescan week {WeekId}", weekId);
            return new WeekRescanResult();
        }
    }

    public async Task<int> CalculateUserGoalAsync(ulong userId, int weekId)
    {
        try
        {
            var totalGoalPoints = await context.MessageLogs
                .Where(ml => ml.UserId == userId && ml.WeekId == weekId)
                .SumAsync(ml => ml.GoalPoints);

            return totalGoalPoints;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to calculate goal for user {UserId} in week {WeekId}", userId, weekId);
            return 0;
        }
    }

    public async Task<List<UserLeaderboardEntry>> CalculateWeeklyLeaderboardAsync(int weekId)
    {
        try
        {
            var leaderboardData = await context.MessageLogs
                .Where(ml => ml.WeekId == weekId)
                .GroupBy(ml => ml.UserId)
                .Select(g => new UserLeaderboardEntry
                {
                    UserId = g.Key,
                    PomodoroPoints = g.Sum(ml => ml.PomodoroPoints),
                    BonusPoints = g.Sum(ml => ml.BonusPoints),
                    GoalPoints = g.Sum(ml => ml.GoalPoints),
                    MessageCount = g.Count()
                })
                .OrderByDescending(x => x.TotalPoints)
                .ToListAsync();

            return leaderboardData;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to calculate leaderboard for week {WeekId}", weekId);
            return new List<UserLeaderboardEntry>();
        }
    }

    public async Task<List<ChallengeLeaderboardEntry>> CalculateChallengeLeaderboardWithWeeklyProgressAsync(int challengeId, int currentWeekId)
    {
        try
        {
            // Get all message logs for this challenge
            var allChallengeMessages = await context.MessageLogs
                .Include(ml => ml.Week)
                .Where(ml => ml.Week.ChallengeId == challengeId)
                .ToListAsync();

            // Get weekly messages for the specific week
            var weeklyMessages = allChallengeMessages
                .Where(ml => ml.WeekId == currentWeekId)
                .ToList();

            // Group by user and calculate total + weekly stats
            var leaderboardData = allChallengeMessages
                .GroupBy(ml => ml.UserId)
                .Select(g => new ChallengeLeaderboardEntry
                {
                    UserId = g.Key,
                    TotalPomodoroPoints = g.Sum(ml => ml.PomodoroPoints),
                    TotalBonusPoints = g.Sum(ml => ml.BonusPoints),
                    TotalGoalPoints = g.Sum(ml => ml.GoalPoints),
                    TotalMessageCount = g.Count(),
                    
                    // Weekly stats for this specific week
                    WeeklyPomodoroPoints = weeklyMessages.Where(ml => ml.UserId == g.Key).Sum(ml => ml.PomodoroPoints),
                    WeeklyBonusPoints = weeklyMessages.Where(ml => ml.UserId == g.Key).Sum(ml => ml.BonusPoints),
                    WeeklyGoalPoints = weeklyMessages.Where(ml => ml.UserId == g.Key).Sum(ml => ml.GoalPoints),
                    WeeklyMessageCount = weeklyMessages.Count(ml => ml.UserId == g.Key)
                })
                .OrderByDescending(x => x.TotalPoints)
                .ToList();

            return leaderboardData;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to calculate challenge leaderboard for challenge {ChallengeId} week {WeekId}", challengeId, currentWeekId);
            return new List<ChallengeLeaderboardEntry>();
        }
    }

    /// <summary>
    /// Process historical messages from a Discord thread for challenge import
    /// </summary>
    public async Task<(int messagesProcessed, List<ulong> usersFound)> ProcessHistoricalMessagesAsync(
        ulong threadId, int challengeId, int weekNumber, int batchSize = 100)
    {
        var messagesProcessed = 0;
        var usersFound = new HashSet<ulong>();

        try
        {
            logger.LogInformation("Starting historical message processing for thread {ThreadId}, challenge {ChallengeId}, week {WeekNumber}", 
                threadId, challengeId, weekNumber);

            // Get the week record
            var week = await context.Weeks
                .Where(w => w.ChallengeId == challengeId && w.WeekNumber == weekNumber)
                .FirstOrDefaultAsync();

            if (week == null)
            {
                logger.LogWarning("Week record not found for challenge {ChallengeId}, week {WeekNumber}", challengeId, weekNumber);
                return (0, new List<ulong>());
            }

            // Get the gateway client from service provider
            var gatewayClient = serviceProvider.GetService<NetCord.Gateway.GatewayClient>();
            if (gatewayClient?.Rest == null)
            {
                logger.LogWarning("Gateway client not available for historical message processing");
                return (0, new List<ulong>());
            }

            // Get the channel/thread
            var channel = await gatewayClient.Rest.GetChannelAsync(threadId);
            if (channel == null)
            {
                logger.LogWarning("Thread/channel {ThreadId} not found or not accessible", threadId);
                return (0, new List<ulong>());
            }

            // Process messages with pagination
            ulong? beforeMessageId = null;
            bool hasMoreMessages = true;
            int totalFetched = 0;
            const int maxBatches = 50; // Prevent infinite loops
            int batchCount = 0;

            while (hasMoreMessages && batchCount < maxBatches)
            {
                batchCount++;
                logger.LogDebug("Fetching message batch {BatchNumber} from thread {ThreadId}", batchCount, threadId);

                try
                {
                    // TODO: Use proper NetCord message API once available
                    // For now, this is a placeholder that demonstrates the structure
                    logger.LogDebug("NetCord message API integration pending - would fetch messages with pagination");
                    
                    // Placeholder for actual message fetching:
                    // var messages = await GetMessagesFromChannelAsync(channel, batchSize, beforeMessageId);
                    
                    // Expected process:
                    // 1. Fetch messages in batches of 100
                    // 2. Parse each message for emoji content
                    // 3. Calculate points using existing emoji service
                    // 4. Create MessageLog entries for eligible messages
                    // 5. Track users and processed message count
                    // 6. Handle pagination with beforeMessageId
                    // 7. Respect Discord rate limits (delay between batches)

                    // For demonstration, simulate processing a small batch
                    var simulatedBatchSize = Math.Min(5, batchSize); // Small simulation
                    var batch = await ProcessSimulatedMessageBatch(simulatedBatchSize, week.Id);
                    messagesProcessed += batch.messagesProcessed;
                    
                    foreach (var userId in batch.usersFound)
                    {
                        usersFound.Add(userId);
                    }

                    totalFetched += simulatedBatchSize;
                    
                    // Simulate pagination ending after a few batches
                    hasMoreMessages = batchCount < 3; // Limit for demonstration
                    
                    logger.LogDebug("Processed {MessageCount} messages in batch {BatchNumber}", batch.messagesProcessed, batchCount);
                    
                    // Rate limiting delay
                    await Task.Delay(250); // Respect Discord rate limits
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error processing message batch {BatchNumber} from thread {ThreadId}", batchCount, threadId);
                    // Continue with next batch
                }
            }

            logger.LogInformation("Completed historical message processing for thread {ThreadId}: {MessageCount} messages, {UserCount} users, {BatchCount} batches", 
                threadId, messagesProcessed, usersFound.Count, batchCount);

            return (messagesProcessed, usersFound.ToList());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process historical messages from thread {ThreadId}", threadId);
            return (0, new List<ulong>());
        }
    }

    /// <summary>
    /// Simulate processing a batch of messages for demonstration purposes
    /// This will be replaced with actual NetCord message fetching
    /// </summary>
    private async Task<(int messagesProcessed, List<ulong> usersFound)> ProcessSimulatedMessageBatch(int batchSize, int weekId)
    {
        var processed = 0;
        var users = new List<ulong>();

        // Simulate processing some emoji messages
        var simulatedMessages = new List<DiscordMessageInfo>
        {
            new()
            {
                MessageId = (ulong)(1000000 + weekId * 100 + processed),
                UserId = 123456789,
                Content = "🍅🍅 Finished my study session!",
                Timestamp = DateTime.UtcNow.AddDays(-7)
            }
        };

        // Limit to requested batch size
        simulatedMessages = simulatedMessages.Take(batchSize).ToList();

        // Process the simulated batch
        processed = await ProcessMessageBatchAsync(simulatedMessages, weekId);
        users = simulatedMessages.Select(m => m.UserId).Distinct().ToList();

        return (processed, users);
    }

    /// <summary>
    /// Process a batch of Discord messages for import (respects rate limits)
    /// </summary>
    public async Task<int> ProcessMessageBatchAsync(List<DiscordMessageInfo> messages, int weekId)
    {
        var processed = 0;

        try
        {
            // Get week details to find serverId and challengeId
            var week = await context.Weeks
                .Include(w => w.Challenge)
                .FirstOrDefaultAsync(w => w.Id == weekId);

            if (week == null)
            {
                logger.LogWarning("Week {WeekId} not found for batch processing", weekId);
                return 0;
            }

            foreach (var messageInfo in messages)
            {
                // Check if message was already processed
                var existingLog = await context.MessageLogs
                    .FirstOrDefaultAsync(ml => ml.MessageId == messageInfo.MessageId);

                if (existingLog != null)
                {
                    logger.LogDebug("Message {MessageId} already processed, skipping", messageInfo.MessageId);
                    continue;
                }

                // Calculate points from message content using emoji service
                var detectionResult = emojiService.DetectEmojis(messageInfo.Content);
                var pointsResult = await CalculatePointsAsync(detectionResult, week.Challenge.ServerId, week.Challenge.Id);

                if (pointsResult.PomodoroPoints > 0 || pointsResult.BonusPoints > 0 || pointsResult.GoalPoints > 0)
                {
                    // Create message log entry
                    var messageLog = new MessageLog
                    {
                        MessageId = messageInfo.MessageId,
                        UserId = messageInfo.UserId,
                        WeekId = weekId,
                        PomodoroPoints = pointsResult.PomodoroPoints,
                        BonusPoints = pointsResult.BonusPoints,
                        GoalPoints = pointsResult.GoalPoints
                    };

                    context.MessageLogs.Add(messageLog);
                    processed++;

                    logger.LogDebug("Processed historical message {MessageId} for user {UserId}: {Pomodoro} pomodoro, {Bonus} bonus, {Goal} goal points",
                        messageInfo.MessageId, messageInfo.UserId, pointsResult.PomodoroPoints, pointsResult.BonusPoints, pointsResult.GoalPoints);
                }

                // Add small delay to respect Discord rate limits during import
                await Task.Delay(50); // 50ms delay between message processing
            }

            if (processed > 0)
            {
                await context.SaveChangesAsync();
                logger.LogInformation("Processed batch of {ProcessedCount} messages for week {WeekId}", processed, weekId);
            }

            return processed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process message batch for week {WeekId}", weekId);
            return 0;
        }
    }

    /// <summary>
    /// Helper class for historical message data during import
    /// </summary>
    public class DiscordMessageInfo
    {
        public ulong MessageId { get; set; }
        public ulong UserId { get; set; }
        public string Content { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Generate a leaderboard embed for a specific week
    /// </summary>
    public async Task<EmbedProperties> GenerateLeaderboardEmbedAsync(int weekId)
    {
        try
        {
            // Get week and challenge information
            var week = await context.Weeks
                .Include(w => w.Challenge)
                .FirstOrDefaultAsync(w => w.Id == weekId);

            if (week == null)
            {
                return new EmbedProperties()
                    .WithTitle("❌ Error")
                    .WithDescription("Week not found")
                    .WithColor(new Color(0xff0000));
            }

            var challenge = week.Challenge;

            // Calculate leaderboard data
            var leaderboardData = await CalculateChallengeLeaderboardWithWeeklyProgressAsync(challenge.Id, week.Id);

            if (!leaderboardData.Any())
            {
                return new EmbedProperties()
                    .WithTitle($"🏆 Challenge Leaderboard - Week {week.WeekNumber}")
                    .WithDescription($"**{challenge.Theme}** - Semester {challenge.SemesterNumber}\n\nNo data found for this week.")
                    .WithColor(new Color(0xffd700))
                    .WithTimestamp(DateTimeOffset.UtcNow);
            }

            // Create the embed
            var embed = new EmbedProperties()
                .WithTitle($"🏆 Challenge Leaderboard - Week {week.WeekNumber}")
                .WithColor(new Color(0xffd700)) // Gold color
                .WithDescription($"**{challenge.Theme}** - Semester {challenge.SemesterNumber}\nRanked by total challenge score with this week's progress")
                .WithTimestamp(DateTimeOffset.UtcNow);

            // Get available reward emojis for this challenge/server
            var rewardEmojis = await context.Emojis
                .Where(e => e.ServerId == challenge.ServerId && e.IsActive && e.EmojiType == EmojiType.Reward)
                .Where(e => e.ChallengeId == null || e.ChallengeId == challenge.Id)
                .OrderBy(e => e.PointValue)
                .ToListAsync();

            // Show ALL participants ranked by total challenge score
            var leaderboardText = string.Empty;
            
            for (int i = 0; i < leaderboardData.Count; i++)
            {
                var entry = leaderboardData[i];
                var rank = i + 1;
                var rankEmoji = GetRankEmoji(rank, rewardEmojis);
                
                // Format user mention and total challenge stats
                var userName = $"<@{entry.UserId}>";
                var totalStats = $"{entry.TotalPoints} pts total";
                
                if (entry.TotalPomodoroPoints > 0 || entry.TotalBonusPoints > 0)
                {
                    totalStats += $" ({entry.TotalPomodoroPoints}🍅";
                    if (entry.TotalBonusPoints > 0)
                        totalStats += $" + {entry.TotalBonusPoints}⭐";
                    totalStats += ")";
                }
                
                // Weekly progress for this week
                var weeklyProgress = "";
                if (entry.WeeklyPoints > 0)
                {
                    weeklyProgress = $" | +{entry.WeeklyPoints} this week";
                    if (entry.WeeklyPomodoroPoints > 0 || entry.WeeklyBonusPoints > 0)
                    {
                        weeklyProgress += $" ({entry.WeeklyPomodoroPoints}🍅";
                        if (entry.WeeklyBonusPoints > 0)
                            weeklyProgress += $" + {entry.WeeklyBonusPoints}⭐";
                        weeklyProgress += ")";
                    }
                }
                
                // Goal achievement indicator using reward emojis
                var goalStatus = "";
                if (entry.TotalGoalAchieved && rewardEmojis.Any())
                {
                    // Use first reward emoji for goal achievement
                    var goalRewardEmoji = rewardEmojis.First().EmojiCode;
                    goalStatus = $" {goalRewardEmoji}";
                }
                else if (entry.TotalGoalPoints > 0 && !entry.TotalGoalAchieved)
                {
                    goalStatus = $" 🎯{entry.TotalGoalPoints}";
                }
                
                leaderboardText += $"{rankEmoji} **{rank}.** {userName} - {totalStats}{weeklyProgress}{goalStatus}\n";
            }
            
            embed.AddFields(new EmbedFieldProperties()
                .WithName("🏆 Challenge Leaderboard")
                .WithValue(leaderboardText.Trim())
                .WithInline(false));

            // Add summary statistics
            var totalParticipants = leaderboardData.Count;
            var totalMessages = leaderboardData.Sum(x => x.TotalMessageCount);
            var totalPoints = leaderboardData.Sum(x => x.TotalPoints);
            var goalsAchieved = leaderboardData.Count(x => x.TotalGoalAchieved);
            var weeklyMessages = leaderboardData.Sum(x => x.WeeklyMessageCount);
            var weeklyPoints = leaderboardData.Sum(x => x.WeeklyPoints);
            
            var summaryText = $"**{totalParticipants}** participants\n" +
                             $"**{totalPoints}** total points | **{weeklyPoints}** this week\n" +
                             $"**{totalMessages}** total messages | **{weeklyMessages}** this week\n" +
                             $"**{goalsAchieved}** goals achieved 🎯";

            embed.AddFields(new EmbedFieldProperties()
                .WithName("📊 Challenge Statistics")
                .WithValue(summaryText)
                .WithInline(false));

            return embed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate leaderboard embed for week {WeekId}", weekId);
            return new EmbedProperties()
                .WithTitle("❌ Error")
                .WithDescription("Failed to generate leaderboard")
                .WithColor(new Color(0xff0000));
        }
    }

    private string GetRankEmoji(int rank, List<Models.Emoji> rewardEmojis)
    {
        return rank switch
        {
            1 => "🥇",
            2 => "🥈", 
            3 => "🥉",
            _ when rank <= 10 => $"🏅",
            _ => "▫️"
        };
    }
}

 