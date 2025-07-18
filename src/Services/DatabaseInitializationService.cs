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
            
            // Apply migrations (this will also create the database if it doesn't exist)
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
                
                // Even with no migrations, call MigrateAsync to ensure database and migrations history table exist
                await context.Database.MigrateAsync(cancellationToken);
                logger.LogInformation("Database and migrations history ensured");
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