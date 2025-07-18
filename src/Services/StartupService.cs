using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PomoChallengeCounter.Services;

public class StartupService(
    IServiceProvider serviceProvider,
    ILogger<StartupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Starting application initialization...");

            // Initialize localization first (other services might need it)
            using var scope = serviceProvider.CreateScope();
            var localizationService = scope.ServiceProvider.GetRequiredService<ILocalizationService>();
            await localizationService.InitializeAsync();

            logger.LogInformation("Application initialization completed successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize application");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Application shutdown initiated");
        return Task.CompletedTask;
    }
} 