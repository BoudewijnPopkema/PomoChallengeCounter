using Microsoft.EntityFrameworkCore;
using PomoChallengeCounter.Models;

namespace PomoChallengeCounter.Data;

public class PomoChallengeDbContext(DbContextOptions<PomoChallengeDbContext> options) : DbContext(options)
{
    // DbSets for all entities
    public DbSet<Server> Servers { get; set; }
    public DbSet<Challenge> Challenges { get; set; }
    public DbSet<Week> Weeks { get; set; }
    public DbSet<Emoji> Emojis { get; set; }
    public DbSet<UserGoal> UserGoals { get; set; }
    public DbSet<MessageLog> MessageLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Server entity
        modelBuilder.Entity<Server>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever(); // Discord IDs are not auto-generated
            entity.HasIndex(e => e.Id).HasDatabaseName("idx_servers_guild");
        });

        // Configure Challenge entity
        modelBuilder.Entity<Challenge>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ServerId).HasDatabaseName("idx_challenges_server");
            entity.HasIndex(e => e.IsCurrent).HasDatabaseName("idx_challenges_current");
            entity.HasIndex(e => e.IsActive).HasDatabaseName("idx_challenges_active");
            
            entity.HasOne(e => e.Server)
                .WithMany(s => s.Challenges)
                .HasForeignKey(e => e.ServerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure Week entity
        modelBuilder.Entity<Week>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ChallengeId).HasDatabaseName("idx_weeks_challenge");
            
            entity.HasOne(e => e.Challenge)
                .WithMany(c => c.Weeks)
                .HasForeignKey(e => e.ChallengeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure Emoji entity
        modelBuilder.Entity<Emoji>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ServerId).HasDatabaseName("idx_emojis_server");
            entity.HasIndex(e => e.ChallengeId).HasDatabaseName("idx_emojis_challenge");
            
            // Convert enum to string for PostgreSQL
            entity.Property(e => e.EmojiType)
                .HasConversion<string>();
            
            entity.HasOne(e => e.Server)
                .WithMany(s => s.Emojis)
                .HasForeignKey(e => e.ServerId)
                .OnDelete(DeleteBehavior.Cascade);
                
            entity.HasOne(e => e.Challenge)
                .WithMany(c => c.Emojis)
                .HasForeignKey(e => e.ChallengeId)
                .OnDelete(DeleteBehavior.Cascade);
        });



        // Configure UserGoal entity
        modelBuilder.Entity<UserGoal>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.WeekId }).HasDatabaseName("idx_goals_user_week");
            
            // Unique constraint for UserId, WeekId
            entity.HasIndex(e => new { e.UserId, e.WeekId })
                .IsUnique()
                .HasDatabaseName("uk_goals_user_week");
            
            entity.HasOne(e => e.Week)
                .WithMany(w => w.UserGoals)
                .HasForeignKey(e => e.WeekId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure MessageLog entity
        modelBuilder.Entity<MessageLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.MessageId)
                .IsUnique()
                .HasDatabaseName("uk_messages_processed");
            entity.HasIndex(e => e.WeekId).HasDatabaseName("idx_messages_week");
            entity.HasIndex(e => new { e.UserId, e.WeekId }).HasDatabaseName("idx_messages_user_week"); // For leaderboard queries
            
            entity.HasOne(e => e.Week)
                .WithMany(w => w.MessageLogs)
                .HasForeignKey(e => e.WeekId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
} 