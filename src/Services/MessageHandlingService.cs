using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetCord.Gateway;

namespace PomoChallengeCounter.Services;

public class MessageHandlingService(
    IServiceProvider serviceProvider,
    ILogger<MessageHandlingService> logger) : IHostedService
{
    private GatewayClient? _gatewayClient;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Starting message handling service...");

            // Get the gateway client from DI
            _gatewayClient = serviceProvider.GetRequiredService<GatewayClient>();
            
            // Subscribe to NetCord message events
            _gatewayClient.MessageCreate += HandleMessageCreateAsync;
            _gatewayClient.MessageUpdate += HandleMessageUpdateAsync;
            _gatewayClient.MessageDelete += HandleMessageDeleteAsync;
            
            logger.LogInformation("Message handling service started successfully - subscribed to MessageCreate, MessageUpdate, and MessageDelete events");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start message handling service");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Stopping message handling service...");
            
            if (_gatewayClient != null)
            {
                _gatewayClient.MessageCreate -= HandleMessageCreateAsync;
                _gatewayClient.MessageUpdate -= HandleMessageUpdateAsync;
                _gatewayClient.MessageDelete -= HandleMessageDeleteAsync;
                logger.LogInformation("Unsubscribed from MessageCreate, MessageUpdate, and MessageDelete events");
            }
            
            logger.LogInformation("Message handling service stopped successfully");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error stopping message handling service");
            return Task.CompletedTask; // Don't throw on shutdown
        }
    }

    private async ValueTask HandleMessageCreateAsync(Message message)
    {
        try
        {
            // Skip bot messages
            if (message.Author.IsBot)
                return;

            logger.LogDebug("Processing message {MessageId} from user {UserId} in channel {ChannelId}", 
                message.Id, message.Author.Id, message.ChannelId);

            // Create a scope for scoped services (MessageProcessorService is scoped)
            using var scope = serviceProvider.CreateScope();
            var messageProcessor = scope.ServiceProvider.GetRequiredService<MessageProcessorService>();

            // Process the message for emoji tracking
            await messageProcessor.ProcessMessageAsync(
                message.Id, 
                message.Author.Id, 
                message.Content, 
                message.ChannelId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing message {MessageId} from user {UserId}", 
                message.Id, message.Author.Id);
            // Don't rethrow - we don't want to kill the gateway connection
        }
    }

    private async ValueTask HandleMessageUpdateAsync(Message message)
    {
        try
        {
            // Skip bot messages
            if (message.Author.IsBot)
                return;

            logger.LogDebug("Processing message update {MessageId} from user {UserId} in channel {ChannelId}", 
                message.Id, message.Author.Id, message.ChannelId);

            // Create a scope for scoped services (MessageProcessorService is scoped)
            using var scope = serviceProvider.CreateScope();
            var messageProcessor = scope.ServiceProvider.GetRequiredService<MessageProcessorService>();

            // Update the message for emoji tracking
            await messageProcessor.UpdateMessageAsync(message.Id, message.Content);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing message update {MessageId} from user {UserId}", 
                message.Id, message.Author.Id);
            // Don't rethrow - we don't want to kill the gateway connection
        }
    }

    private async ValueTask HandleMessageDeleteAsync(MessageDeleteEventArgs args)
    {
        try
        {
            logger.LogDebug("Processing message deletion {MessageId} in channel {ChannelId}", 
                args.MessageId, args.ChannelId);

            // Create a scope for scoped services (MessageProcessorService is scoped)
            using var scope = serviceProvider.CreateScope();
            var messageProcessor = scope.ServiceProvider.GetRequiredService<MessageProcessorService>();

            // Delete the message from tracking
            await messageProcessor.DeleteMessageAsync(args.MessageId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing message deletion {MessageId}", args.MessageId);
            // Don't rethrow - we don't want to kill the gateway connection
        }
    }
} 