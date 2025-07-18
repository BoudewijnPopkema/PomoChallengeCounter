using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using PomoChallengeCounter.Models.Results;

namespace PomoChallengeCounter.Services;

/// <summary>
/// Implementation of Discord thread operations using NetCord
/// </summary>
public class DiscordThreadService(
    GatewayClient gatewayClient,
    ILogger<DiscordThreadService> logger) : IDiscordThreadService
{
    public async Task<DiscordThreadResult> CreateThreadAsync(ulong serverId, ulong categoryId, string threadName, int weekNumber, string? welcomeMessage = null,
        ulong? pingRoleId = null)
    {
        try
        {
            logger.LogInformation("Creating thread {ThreadName} for server {ServerId} in category {CategoryId}", threadName, serverId, categoryId);

            // Find a suitable channel in the category
            var channelResult = await FindChannelInCategoryAsync(serverId, categoryId);
            if (!channelResult.IsSuccess)
            {
                return DiscordThreadResult.Failure($"Failed to find channel: {channelResult.ErrorMessage}");
            }

            // Get the channel for thread creation
            var channel = await gatewayClient.Rest.GetChannelAsync(channelResult.ChannelId);
            if (channel is not TextGuildChannel textChannel)
            {
                return DiscordThreadResult.Failure("Channel is not a text channel that supports threads");
            }

            // Create thread properties
            var threadProperties = new GuildThreadProperties(threadName)
            {
                ChannelType = ChannelType.PublicGuildThread,
            };

            // Create the actual Discord thread
            var createdThread = await textChannel.CreateGuildThreadAsync(threadProperties);

            logger.LogInformation("Successfully created Discord thread {ThreadName} (ID: {ThreadId}) in channel {ChannelName}",
                threadName, createdThread.Id, textChannel.Name);

            // Send welcome message if provided
            var messageSent = false;
            if (!string.IsNullOrEmpty(welcomeMessage))
            {
                messageSent = await SendMessageToThreadAsync(createdThread.Id, welcomeMessage, pingRoleId);
            }

            return DiscordThreadResult.Success(createdThread.Id, threadName, messageSent);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create thread {ThreadName} for server {ServerId}", threadName, serverId);
            return DiscordThreadResult.Failure($"Thread creation failed: {ex.Message}");
        }
    }

    public async Task<DiscordThreadResult> CreateChallengeThreadAsync(ulong serverId, ulong categoryId, string threadName, int weekNumber, string challengeTheme, int semesterNumber, string? welcomeMessage = null, ulong? pingRoleId = null)
    {
        try
        {
            logger.LogInformation("Creating challenge thread {ThreadName} for challenge Q{SemesterNumber} '{Theme}' in server {ServerId}", 
                threadName, semesterNumber, challengeTheme, serverId);

            // First try to find an existing challenge channel
            var channelResult = await FindChannelForChallengeAsync(serverId, categoryId, challengeTheme, semesterNumber);
            
            // If no suitable channel found, create a new challenge channel
            if (!channelResult.IsSuccess)
            {
                logger.LogInformation("No suitable channel found for challenge Q{SemesterNumber} '{Theme}', creating new channel", 
                    semesterNumber, challengeTheme);
                    
                channelResult = await CreateChallengeChannelAsync(serverId, categoryId, challengeTheme, semesterNumber);
                
                if (!channelResult.IsSuccess)
                {
                    return DiscordThreadResult.Failure($"Failed to create challenge channel: {channelResult.ErrorMessage}");
                }
            }

            // Get the channel for thread creation
            var channel = await gatewayClient.Rest.GetChannelAsync(channelResult.ChannelId);
            if (channel is not TextGuildChannel textChannel)
            {
                return DiscordThreadResult.Failure("Channel is not a text channel that supports threads");
            }

            // Create thread properties (public thread by default)
            var threadProperties = new GuildThreadProperties(threadName);

            // Create the actual Discord thread
            var createdThread = await textChannel.CreateGuildThreadAsync(threadProperties);

            logger.LogInformation("Successfully created challenge thread {ThreadName} (ID: {ThreadId}) in channel {ChannelName} for Q{SemesterNumber}",
                threadName, createdThread.Id, textChannel.Name, semesterNumber);

            // Send welcome message if provided
            var messageSent = false;
            if (!string.IsNullOrEmpty(welcomeMessage))
            {
                messageSent = await SendMessageToThreadAsync(createdThread.Id, welcomeMessage, pingRoleId);
            }

            return DiscordThreadResult.Success(createdThread.Id, threadName, messageSent);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create challenge thread {ThreadName} for Q{SemesterNumber} '{Theme}' in server {ServerId}", 
                threadName, semesterNumber, challengeTheme, serverId);
            return DiscordThreadResult.Failure($"Challenge thread creation failed: {ex.Message}");
        }
    }

    public async Task<DiscordChannelResult> FindChannelForChallengeAsync(ulong serverId, ulong categoryId, string challengeTheme, int semesterNumber)
    {
        try
        {
            logger.LogDebug("Finding channel for challenge Q{SemesterNumber} '{Theme}' in server {ServerId}", semesterNumber, challengeTheme, serverId);

            var guild = await gatewayClient.Rest.GetGuildAsync(serverId);

            var channels = await guild.GetChannelsAsync();

            // Filter text channels in the specified category using ParentId
            var textChannels = channels.OfType<TextGuildChannel>()
                .Where(c => c.ParentId == categoryId)
                .ToList();

            if (!textChannels.Any())
            {
                return DiscordChannelResult.Failure("No text channels found in the specified category");
            }

            // Try to find channel matching challenge theme or semester
            var themeKeywords = challengeTheme.ToLowerInvariant().Replace(" ", "-");
            var semesterKeywords = $"q{semesterNumber}";

            var matchingChannel = textChannels.FirstOrDefault(c =>
                c.Name.Contains(themeKeywords) ||
                c.Name.Contains(semesterKeywords) ||
                c.Name.Contains("challenge"));

            if (matchingChannel != null)
            {
                logger.LogDebug("Found matching channel {ChannelName} for challenge in category {CategoryId}",
                    matchingChannel.Name, categoryId);
                return DiscordChannelResult.Success(matchingChannel.Id, matchingChannel.Name);
            }

            // Fallback to first text channel in category
            var firstTextChannel = textChannels.FirstOrDefault();
            if (firstTextChannel != null)
            {
                logger.LogDebug("Using fallback channel {ChannelName} for challenge in category {CategoryId}",
                    firstTextChannel.Name, categoryId);
                return DiscordChannelResult.Success(firstTextChannel.Id, firstTextChannel.Name);
            }

            return DiscordChannelResult.Failure("No suitable text channels found");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to find channel for challenge in server {ServerId}", serverId);
            return DiscordChannelResult.Failure($"Channel search failed: {ex.Message}");
        }
    }

    public async Task<bool> SendMessageToThreadAsync(ulong threadId, string content, ulong? pingRoleId = null)
    {
        try
        {
            var channel = await gatewayClient.Rest.GetChannelAsync(threadId);
            if (channel is not TextGuildChannel threadChannel)
            {
                logger.LogWarning("Thread {ThreadId} is not a text channel", threadId);
                return false;
            }

            // Build message content with optional role ping
            var messageContent = content;
            if (pingRoleId.HasValue)
            {
                messageContent = $"<@&{pingRoleId.Value}> {content}";
            }

            var messageProperties = new MessageProperties
            {
                Content = messageContent
            };

            await threadChannel.SendMessageAsync(messageProperties);
            logger.LogDebug("Sent message to thread {ThreadId}", threadId);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send message to thread {ThreadId}", threadId);
            return false;
        }
    }

    public async Task<DiscordThreadInfo?> GetThreadInfoAsync(ulong threadId)
    {
        try
        {
            var channel = await gatewayClient.Rest.GetChannelAsync(threadId);
            if (channel is not GuildThread thread)
            {
                return null;
            }

            return new DiscordThreadInfo
            {
                ThreadId = thread.Id,
                Name = thread.Name,
                ChannelId = thread.ParentId ?? 0, // For threads, ParentId is the channel they're created in
                IsArchived = thread.Metadata.Archived,
                IsLocked = thread.Metadata.Locked,
                CreatedAt = thread.CreatedAt.DateTime,
                MessageCount = thread.MessageCount
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get thread info for {ThreadId}", threadId);
            return null;
        }
    }

    public async Task<bool> ThreadExistsAsync(ulong threadId)
    {
        try
        {
            var channel = await gatewayClient.Rest.GetChannelAsync(threadId);
            return channel is GuildThread;
        }
        catch
        {
            return false;
        }
    }

    public async Task<DiscordChannelResult> CreateChallengeChannelAsync(ulong serverId, ulong categoryId, string challengeTheme, int semesterNumber)
    {
        try
        {
            logger.LogInformation("Creating challenge channel for Q{SemesterNumber} '{Theme}' in server {ServerId}, category {CategoryId}",
                semesterNumber, challengeTheme, serverId, categoryId);

            var guild = await gatewayClient.Rest.GetGuildAsync(serverId);

            // Create a normalized channel name
            var channelName = $"q{semesterNumber}-{challengeTheme.ToLowerInvariant().Replace(" ", "-").Replace("_", "-")}-challenge";

            // Ensure the name meets Discord requirements (alphanumeric, dashes, underscores only)
            channelName = System.Text.RegularExpressions.Regex.Replace(channelName, @"[^a-z0-9\-_]", "");

            // Ensure it's within Discord's length limits (2-100 characters)
            if (channelName.Length < 2)
            {
                channelName = $"q{semesterNumber}-challenge";
            }

            if (channelName.Length > 100)
            {
                channelName = channelName.Substring(0, 100);
            }

            // Check if a channel with this name already exists in the category
            var existingChannels = await guild.GetChannelsAsync();
            var existingChannel = existingChannels
                .OfType<TextGuildChannel>()
                .FirstOrDefault(c => c.ParentId == categoryId &&
                                     string.Equals(c.Name, channelName, StringComparison.OrdinalIgnoreCase));

            if (existingChannel != null)
            {
                logger.LogInformation("Channel {ChannelName} already exists in category {CategoryId}, using existing channel",
                    channelName, categoryId);
                return DiscordChannelResult.Success(existingChannel.Id, existingChannel.Name);
            }

            // Create the channel using NetCord REST API
            var channelTopic = $"Q{semesterNumber} Challenge: {challengeTheme} | Weekly threads and progress tracking";
            var channelProperties = new GuildChannelProperties(channelName, ChannelType.TextGuildChannel)
            {
                ParentId = categoryId,
                Topic = channelTopic
            };

            logger.LogInformation("Creating Discord text channel {ChannelName} in category {CategoryId}", channelName, categoryId);

            var createdChannel = await guild.CreateChannelAsync(channelProperties);

            logger.LogInformation("Successfully created challenge channel {ChannelName} (ID: {ChannelId}) for Q{SemesterNumber}",
                channelName, createdChannel.Id, semesterNumber);

            return DiscordChannelResult.Success(createdChannel.Id, createdChannel.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create challenge channel for Q{SemesterNumber} '{Theme}' in server {ServerId}",
                semesterNumber, challengeTheme, serverId);
            return DiscordChannelResult.Failure($"Channel creation failed: {ex.Message}");
        }
    }

    private async Task<DiscordChannelResult> FindChannelInCategoryAsync(ulong serverId, ulong categoryId)
    {
        try
        {
            var guild = await gatewayClient.Rest.GetGuildAsync(serverId);

            var channels = await guild.GetChannelsAsync();
            var textChannel = channels
                .OfType<TextGuildChannel>()
                .FirstOrDefault(c => c.ParentId == categoryId);

            if (textChannel == null)
            {
                // Fallback to any text channel if none found in category
                textChannel = channels.OfType<TextGuildChannel>().FirstOrDefault();
            }

            if (textChannel == null)
            {
                return DiscordChannelResult.Failure("No text channels found");
            }

            return DiscordChannelResult.Success(textChannel.Id, textChannel.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to find channel for server {ServerId}", serverId);
            return DiscordChannelResult.Failure($"Channel search failed: {ex.Message}");
        }
    }
}