using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using PomoChallengeCounter.Data;
using PomoChallengeCounter.Services;
using Serilog;

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

            var host = CreateHostBuilder(args).Build();
            
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

    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseSerilog()
            .ConfigureServices((context, services) =>
            {
                // Configure Entity Framework DbContext
                var connectionString = BuildConnectionString(context.Configuration);
                services.AddDbContext<PomoChallengeDbContext>(options =>
                    options.UseNpgsql(connectionString));
                
                // Register core services
                services.AddSingleton<LocalizationService>();
                services.AddSingleton<EmojiService>();
                services.AddScoped<MessageProcessorService>();
                
                // Register hosted services in startup order
                services.AddHostedService<DatabaseInitializationService>(); // 1. Database first
                services.AddHostedService<StartupService>();               // 2. App initialization
                services.AddHostedService<DiscordBotService>();            // 3. Discord bot last
                
                // TODO: Configure remaining services
                // - Command handlers
                // - Message processors
                // - Background schedulers
                
                Log.Information("Services configured successfully");
            });

    private static string BuildConnectionString(IConfiguration configuration)
    {
        var host = configuration["DATABASE_HOST"] ?? "localhost";
        var database = configuration["DATABASE_NAME"] ?? "pomodoro_bot";
        var username = configuration["DATABASE_USER"] ?? "pomodoro_user";
        var password = configuration["DATABASE_PASSWORD"] ?? throw new InvalidOperationException("DATABASE_PASSWORD environment variable is required");

        return $"Host={host};Database={database};Username={username};Password={password};";
    }
} 