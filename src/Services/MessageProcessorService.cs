using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PomoChallengeCounter.Data;
using PomoChallengeCounter.Models;
using PomoChallengeCounter.Models.Results;

namespace PomoChallengeCounter.Services;

public class MessageProcessorService(
    PomoChallengeDbContext context,
    IEmojiService emojiService,
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
            var matchingEmoji = emojis.FirstOrDefault(e => e.EmojiCode == detectedEmoji);
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
}

 