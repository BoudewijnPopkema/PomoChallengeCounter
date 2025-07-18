using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using PomoChallengeCounter.Data;
using PomoChallengeCounter.Services;
using Serilog;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Services.ApplicationCommands;
using NetCord.Rest;

namespace PomoChallengeCounter;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .WriteTo.File("logs/pomodoro-bot-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
            .CreateLogger();

        try
        {
            Log.Information("Starting PomoChallengeCounter Discord Bot...");

            var builder = Host.CreateApplicationBuilder(args);
            
            // Add Serilog
            builder.Services.AddSerilog();

            // Configure Discord Gateway and Application Commands in the correct order
            var token = builder.Configuration["DISCORD_TOKEN"] ?? 
                throw new InvalidOperationException("DISCORD_TOKEN environment variable is required");

            builder.Services.AddDiscordGateway(options =>
            {
                options.Token = token;
                options.Intents = GatewayIntents.Guilds | 
                                 GatewayIntents.GuildMessages | 
                                 GatewayIntents.MessageContent;
            });

            // Add Application Commands service AFTER Discord Gateway
            builder.Services.AddApplicationCommands();

            // Configure Entity Framework DbContext
            var connectionString = BuildConnectionString(builder.Configuration);
            builder.Services.AddDbContext<PomoChallengeDbContext>(options =>
                options.UseNpgsql(connectionString));
            
            // Register core services
            builder.Services.AddSingleton<ITimeProvider, SystemTimeProvider>();
            builder.Services.AddSingleton<ILocalizationService, LocalizationService>();
            builder.Services.AddSingleton<IEmojiService, EmojiService>();
            builder.Services.AddScoped<MessageProcessorService>();
            builder.Services.AddScoped<IChallengeService, ChallengeService>();
            builder.Services.AddScoped<IDiscordThreadService, DiscordThreadService>();
            
            // Register hosted services in startup order
            builder.Services.AddHostedService<DatabaseInitializationService>(); // 1. Database first
            builder.Services.AddHostedService<StartupService>();               // 2. App initialization
            builder.Services.AddHostedService<MessageHandlingService>();       // 3. Message event handling
            builder.Services.AddHostedService<AutomationService>();            // 4. Weekly automation

            var host = builder.Build();
            
            // Add modules for command discovery
            host.AddModules(typeof(Program).Assembly);
            
            // Enable NetCord gateway handlers for command processing
            host.UseGatewayHandlers();

            // Register commands with Discord after everything is set up
            await RegisterCommandsAsync(host);
            
            Log.Information("Services configured successfully");
            
            await host.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static async Task RegisterCommandsAsync(IHost host)
    {
        try
        {
            Log.Information("Registering commands with Discord...");
            
            // Get the required services
            var gatewayClient = host.Services.GetRequiredService<GatewayClient>();
            var applicationCommandService = host.Services.GetRequiredService<ApplicationCommandService<ApplicationCommandContext>>();
            
            // Wait a bit to ensure the gateway is ready
            await Task.Delay(2000);
            
            // Register commands globally (you can change this to a specific guild ID for testing)
            await applicationCommandService.RegisterCommandsAsync(gatewayClient.Rest, gatewayClient.Id);
            
            Log.Information("Commands registered successfully with Discord");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to register commands with Discord");
            throw;
        }
    }

    private static string BuildConnectionString(IConfiguration configuration)
    {
        var host = configuration["DATABASE_HOST"] ?? "localhost";
        var database = configuration["DATABASE_NAME"] ?? "pomodoro_bot";
        var username = configuration["DATABASE_USER"] ?? "pomodoro_user";
        var password = configuration["DATABASE_PASSWORD"] ?? throw new InvalidOperationException("DATABASE_PASSWORD environment variable is required");

        return $"Host={host};Database={database};Username={username};Password={password};";
    }
} 