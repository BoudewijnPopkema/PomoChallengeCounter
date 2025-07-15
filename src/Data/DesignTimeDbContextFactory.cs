using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PomoChallengeCounter.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PomoChallengeDbContext>
{
    public PomoChallengeDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PomoChallengeDbContext>();
        
        // Use a default connection string for design-time operations
        // This will be overridden at runtime with actual environment variables
        optionsBuilder.UseNpgsql("Host=localhost;Database=pomodoro_bot;Username=pomodoro_user;Password=dev_password;");
        
        return new PomoChallengeDbContext(optionsBuilder.Options);
    }
} 