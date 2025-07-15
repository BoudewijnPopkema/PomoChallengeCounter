using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PomoChallengeCounter.Data;

namespace PomoChallengeCounter.Services;

public class DatabaseInitializationService(IServiceProvider serviceProvider, ILogger<DatabaseInitializationService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PomoChallengeDbContext>();
            
            logger.LogInformation("Starting database initialization...");
            
            // Apply any pending migrations
            var pendingMigrations = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            if (pendingMigrations.Any())
            {
                logger.LogInformation("Applying {Count} pending migrations: {Migrations}",
                    pendingMigrations.Count, string.Join(", ", pendingMigrations));
                
                await context.Database.MigrateAsync(cancellationToken);
                logger.LogInformation("Database migrations applied successfully");
            }
            else
            {
                logger.LogInformation("No pending migrations found");
            }

            // Ensure database is created (if it doesn't exist)
            var created = await context.Database.EnsureCreatedAsync(cancellationToken);
            if (created)
            {
                logger.LogInformation("Database created successfully");
            }
            else
            {
                logger.LogInformation("Database already exists");
            }

            logger.LogInformation("Database initialization completed successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize database");
            throw; // This will cause the application to fail to start if database init fails
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
} 