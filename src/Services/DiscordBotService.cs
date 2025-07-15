using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PomoChallengeCounter.Services;

public class DiscordBotService(
    IConfiguration configuration,
    ILogger<DiscordBotService> logger) : IHostedService
{
    private DiscordSocketClient? _client;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var token = configuration["DISCORD_TOKEN"] 
                ?? throw new InvalidOperationException("DISCORD_TOKEN environment variable is required");

            logger.LogInformation("Starting Discord bot service...");

            var config = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds | 
                               GatewayIntents.GuildMessages | 
                               GatewayIntents.MessageContent,
                LogLevel = LogSeverity.Info
            };

            _client = new DiscordSocketClient(config);

            // Wire up event handlers
            _client.Log += LogAsync;
            _client.Ready += ReadyAsync;
            _client.MessageReceived += MessageReceivedAsync;
            _client.MessageUpdated += MessageUpdatedAsync;
            _client.MessageDeleted += MessageDeletedAsync;

            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();

            logger.LogInformation("Discord bot started successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start Discord bot");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_client != null)
        {
            logger.LogInformation("Stopping Discord bot...");
            await _client.StopAsync();
            await _client.DisposeAsync();
            logger.LogInformation("Discord bot stopped");
        }
    }

    private Task LogAsync(LogMessage log)
    {
        var severity = log.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            LogSeverity.Debug => LogLevel.Trace,
            _ => LogLevel.Information
        };

        logger.Log(severity, log.Exception, "[Discord.NET] {Message}", log.Message);
        return Task.CompletedTask;
    }

    private Task ReadyAsync()
    {
        logger.LogInformation("Discord bot is ready! Connected as {Username}#{Discriminator}", 
            _client?.CurrentUser?.Username, _client?.CurrentUser?.Discriminator);
        return Task.CompletedTask;
    }

    private async Task MessageReceivedAsync(SocketMessage message)
    {
        // TODO: Process new messages for emoji tracking
        if (message.Author.IsBot) return;
        
        logger.LogDebug("Message received from {User} in {Channel}: {Content}", 
            message.Author.Username, message.Channel.Name, message.Content);
    }

    private async Task MessageUpdatedAsync(Cacheable<IMessage, ulong> before, SocketMessage after, ISocketMessageChannel channel)
    {
        // TODO: Process message edits for emoji tracking
        if (after.Author.IsBot) return;
        
        logger.LogDebug("Message edited by {User} in {Channel}", 
            after.Author.Username, channel.Name);
    }

    private async Task MessageDeletedAsync(Cacheable<IMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel)
    {
        // TODO: Remove deleted message from tracking
        logger.LogDebug("Message deleted in {Channel}", channel.Value?.Name ?? "Unknown");
    }
} 