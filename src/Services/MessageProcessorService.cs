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
    ILocalizationService localizationService,
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

            // Get next week's goal (from current week's goal emojis)
            var nextWeekGoals = weeklyMessages
                .GroupBy(ml => ml.UserId)
                .ToDictionary(g => g.Key, g => g.Sum(ml => ml.GoalPoints));

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
                    WeeklyMessageCount = weeklyMessages.Count(ml => ml.UserId == g.Key),
                    
                    // Goal for next week (from current week's goal emojis)
                    NextWeekGoalPoints = nextWeekGoals.GetValueOrDefault(g.Key, 0)
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
                    // Fetch messages using NetCord REST API
                    var messages = new List<DiscordMessageInfo>();
                    
                    // Use GetMessagesAsync - start with basic approach then add pagination
                    if (channel is NetCord.TextChannel textChannel)
                    {
                        var messageCount = 0;
                        await foreach (var message in textChannel.GetMessagesAsync())
                        {
                            // Skip messages that are newer than our pagination point
                            if (beforeMessageId.HasValue && message.Id >= beforeMessageId.Value)
                                continue;
                            
                            // Convert NetCord RestMessage to our DiscordMessageInfo format
                            var messageInfo = new DiscordMessageInfo
                            {
                                MessageId = message.Id,
                                UserId = message.Author.Id,
                                Content = message.Content ?? string.Empty,
                                Timestamp = message.CreatedAt.DateTime
                            };
                            
                            messages.Add(messageInfo);
                            messageCount++;
                            
                            // Track the oldest message ID for next pagination
                            beforeMessageId = message.Id;
                            
                            // Break if we've hit our batch size limit
                            if (messageCount >= batchSize)
                                break;
                        }
                    }
                    else
                    {
                        logger.LogWarning("Channel {ChannelId} is not a TextChannel, cannot fetch messages", threadId);
                        hasMoreMessages = false;
                        break;
                    }

                    // Process the fetched batch
                    var batchProcessed = await ProcessMessageBatchAsync(messages, week.Id);
                    messagesProcessed += batchProcessed;
                    
                    foreach (var message in messages)
                    {
                        usersFound.Add(message.UserId);
                    }

                    totalFetched += messages.Count;
                    
                    // Check if we got fewer messages than requested (end of channel)
                    hasMoreMessages = messages.Count >= batchSize;
                    
                    logger.LogDebug("Processed {MessageCount} messages in batch {BatchNumber}", batchProcessed, batchCount);
                    
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
        Week? week = null;
        try
        {
            // Get week and challenge information
            week = await context.Weeks
                .Include(w => w.Challenge)
                .FirstOrDefaultAsync(w => w.Id == weekId);

            if (week == null)
            {
                return new EmbedProperties()
                    .WithTitle(localizationService.GetString("leaderboard.error_title", "en"))
                    .WithDescription(localizationService.GetString("leaderboard.error_description", "en", weekId))
                    .WithColor(new Color(0xff0000));
            }

            var challenge = week.Challenge;
            var serverLanguage = challenge.Server.Language;

            // Calculate leaderboard data
            var leaderboardData = await CalculateChallengeLeaderboardWithWeeklyProgressAsync(challenge.Id, week.Id);

            if (!leaderboardData.Any())
            {
                return new EmbedProperties()
                    .WithTitle(localizationService.GetString("leaderboard.title", serverLanguage, week.WeekNumber))
                    .WithDescription($"**{challenge.Theme}** - Semester {challenge.SemesterNumber}\n\n{localizationService.GetString("leaderboard.no_data", serverLanguage)}")
                    .WithColor(new Color(0xffd700))
                    .WithTimestamp(DateTimeOffset.UtcNow);
            }

            // Create the embed with enhanced NetCord features and localization
            var embed = new EmbedProperties()
                .WithTitle(localizationService.GetString("leaderboard.title", serverLanguage, week.WeekNumber))
                .WithColor(new Color(0xffd700)) // Gold color
                .WithDescription(localizationService.GetString("leaderboard.description", serverLanguage, challenge.Theme, challenge.SemesterNumber))
                .WithTimestamp(DateTimeOffset.UtcNow)
                .WithAuthor(new EmbedAuthorProperties()
                    .WithName(localizationService.GetString("leaderboard.author_name", serverLanguage, challenge.SemesterNumber))); 
                    // Note: Icon URLs omitted - would need actual URLs to valid Discord CDN images

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
                
                // Format user mention with reward emoji prefix if they achieved their goal
                var userName = $"<@{entry.UserId}>";
                if (entry.WeeklyGoalAchieved)
                {
                    var rewardEmoji = GetRandomRewardEmoji(rewardEmojis);
                    if (!string.IsNullOrEmpty(rewardEmoji))
                        userName = $"{rewardEmoji} {userName}";
                }
                
                var totalStats = $"{entry.TotalPoints} {localizationService.GetString("leaderboard.points_total", serverLanguage)}";
                
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
                    weeklyProgress = $" | +{entry.WeeklyPoints} {localizationService.GetString("leaderboard.this_week", serverLanguage)}";
                    if (entry.WeeklyPomodoroPoints > 0 || entry.WeeklyBonusPoints > 0)
                    {
                        weeklyProgress += $" ({entry.WeeklyPomodoroPoints}🍅";
                        if (entry.WeeklyBonusPoints > 0)
                            weeklyProgress += $" + {entry.WeeklyBonusPoints}⭐";
                        weeklyProgress += ")";
                    }
                }
                
                // Next week goal display
                var nextWeekGoal = "";
                if (entry.NextWeekGoalPoints > 0)
                {
                    nextWeekGoal = $" - {localizationService.GetString("leaderboard.goal_next_week", serverLanguage, entry.NextWeekGoalPoints)}";
                }
                
                leaderboardText += $"**{rank}.** {userName} - {totalStats}{weeklyProgress}{nextWeekGoal}\n";
            }
            
            embed.AddFields(new EmbedFieldProperties()
                .WithName(localizationService.GetString("leaderboard.field_title", serverLanguage))
                .WithValue(leaderboardText.Trim())
                .WithInline(false));

            // Add summary statistics
            var totalParticipants = leaderboardData.Count;
            var totalMessages = leaderboardData.Sum(x => x.TotalMessageCount);
            var totalPoints = leaderboardData.Sum(x => x.TotalPoints);
            var goalsAchieved = leaderboardData.Count(x => x.TotalGoalAchieved);
            var weeklyMessages = leaderboardData.Sum(x => x.WeeklyMessageCount);
            var weeklyPoints = leaderboardData.Sum(x => x.WeeklyPoints);
            
            var summaryText = $"**{totalParticipants}** {localizationService.GetString("leaderboard.participants", serverLanguage)}\n" +
                             $"**{totalPoints}** {localizationService.GetString("leaderboard.total_points", serverLanguage)} | **{weeklyPoints}** {localizationService.GetString("leaderboard.this_week", serverLanguage)}\n" +
                             $"**{totalMessages}** {localizationService.GetString("leaderboard.total_messages", serverLanguage)} | **{weeklyMessages}** {localizationService.GetString("leaderboard.this_week", serverLanguage)}\n" +
                             $"**{goalsAchieved}** {localizationService.GetString("leaderboard.goals_achieved", serverLanguage)} 🎯";

            embed.AddFields(new EmbedFieldProperties()
                .WithName(localizationService.GetString("leaderboard.statistics_title", serverLanguage))
                .WithValue(summaryText)
                .WithInline(false));

            // Add motivational footer with dynamic content
            var footerText = GetMotivationalFooter(totalParticipants, weeklyPoints, goalsAchieved);
            var footerIcon = GetFooterIcon(totalParticipants);
            
            embed.WithFooter(new EmbedFooterProperties()
                .WithText(footerText)
                .WithIconUrl(footerIcon));

            return embed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate leaderboard embed for week {WeekId}", weekId);
            // Fallback to English for error messages if server language is not available
            var language = week?.Challenge?.Server?.Language ?? "en";
            return new EmbedProperties()
                .WithTitle(localizationService.GetString("leaderboard.error_title", language))
                .WithDescription(localizationService.GetString("leaderboard.error_description", language, weekId))
                .WithColor(new Color(0xff0000))
                .WithTimestamp(DateTimeOffset.UtcNow)
                .WithFooter(new EmbedFooterProperties()
                    .WithText(localizationService.GetString("leaderboard.error_footer", language)));
        }
    }

    private static string GetRandomRewardEmoji(List<Models.Emoji> rewardEmojis)
    {
        // Return a random reward emoji for goal achievers
        if (!rewardEmojis.Any())
            return string.Empty;
            
        var random = new Random();
        var selectedEmoji = rewardEmojis[random.Next(rewardEmojis.Count)];
        return selectedEmoji.EmojiCode;
    }

    private static string GetMotivationalFooter(int participants, int weeklyPoints, int goalsAchieved)
    {
        var participationLevel = participants switch
        {
            >= 20 => "Epic",
            >= 15 => "Amazing", 
            >= 8 => "Great",  // Adjusted: 8+ participants = "Great"
            >= 3 => "Good",   // Adjusted: 3+ participants = "Good" 
            _ => "Growing"
        };

        // Override to "Epic" for high weekly points regardless of participant count
        if (weeklyPoints >= 100)
        {
            participationLevel = "Epic";
        }

        var achievementRate = participants > 0 ? (double)goalsAchieved / participants : 0;
        var achievementEmoji = achievementRate switch
        {
            >= 0.8 => "🔥",
            >= 0.6 => "⭐",
            >= 0.4 => "💪", 
            >= 0.2 => "📈",
            _ => "🌱"
        };

        return weeklyPoints switch
        {
            >= 100 => $"{participationLevel} productivity this week! {achievementEmoji} Keep the momentum going!",
            >= 50 => $"{participationLevel} effort this week! {achievementEmoji} You're making progress!",
            >= 20 => $"{participationLevel} start! {achievementEmoji} Every session counts!",
            _ => $"Building momentum! {achievementEmoji} Every step forward matters!"
        };
    }

    private static string GetFooterIcon(int participants)
    {
        // Return null to avoid invalid URL errors - could be configured with actual Discord CDN URLs
        return null!;
    }
}

 