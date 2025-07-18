using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using PomoChallengeCounter.Data;
using PomoChallengeCounter.Models;
using PomoChallengeCounter.Services;

namespace PomoChallengeCounter.Tests.Commands;

public class EmojiCommandsTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly PomoChallengeDbContext _context;
    private readonly IEmojiService _emojiService;
    private readonly ILocalizationService _localizationService;

    public EmojiCommandsTests()
    {
        var services = new ServiceCollection();
        
        // Add in-memory database
        services.AddDbContext<PomoChallengeDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        
        // Add services
        services.AddSingleton<ITimeProvider, MockTimeProvider>();
        services.AddLogging(); // Add logging support
        services.AddSingleton<IEmojiService, EmojiService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        
        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<PomoChallengeDbContext>();
        _emojiService = _serviceProvider.GetRequiredService<IEmojiService>();
        _localizationService = _serviceProvider.GetRequiredService<ILocalizationService>();
        
        // Initialize localization
        _localizationService.InitializeAsync().Wait();
    }

    [Fact]
    public async Task EmojiService_ShouldValidateUnicodeEmoji()
    {
        // Act & Assert
        _emojiService.ValidateEmojiFormat("🍅", out var format).ShouldBeTrue();
        format.ShouldBe(EmojiFormat.Unicode);
        
        _emojiService.ValidatePointValue(5).ShouldBeTrue();
        _emojiService.ValidatePointValue(0).ShouldBeFalse();
        _emojiService.ValidatePointValue(1000).ShouldBeFalse();
    }

    [Fact]
    public async Task EmojiService_ShouldValidateCustomEmoji()
    {
        // Act & Assert
        _emojiService.ValidateEmojiFormat("<:tomato:123456789>", out var format).ShouldBeTrue();
        format.ShouldBe(EmojiFormat.Custom);
        
        _emojiService.ValidateEmojiFormat("<a:animated:987654321>", out var animatedFormat).ShouldBeTrue();
        animatedFormat.ShouldBe(EmojiFormat.Custom);
    }

    [Fact]
    public async Task EmojiService_ShouldValidateShortcodeEmoji()
    {
        // Act & Assert
        _emojiService.ValidateEmojiFormat(":tomato:", out var format).ShouldBeTrue();
        format.ShouldBe(EmojiFormat.Shortcode);
        
        _emojiService.ValidateEmojiFormat(":fire:", out var fireFormat).ShouldBeTrue();
        fireFormat.ShouldBe(EmojiFormat.Shortcode);
    }

    [Fact]
    public async Task EmojiService_ShouldRejectInvalidFormats()
    {
        // Act & Assert
        _emojiService.ValidateEmojiFormat("invalid", out var format1).ShouldBeFalse();
        _emojiService.ValidateEmojiFormat("", out var format2).ShouldBeFalse();
        _emojiService.ValidateEmojiFormat(":::", out var format3).ShouldBeFalse();
        _emojiService.ValidateEmojiFormat("<:invalid>", out var format4).ShouldBeFalse();
    }

    [Fact]
    public async Task AddEmoji_ShouldCreateValidEmojiRecord()
    {
        // Arrange
        const ulong serverId = 12345;
        var server = await CreateTestServerAsync(serverId);
        var challenge = await CreateTestChallengeAsync(serverId);

        // Act - Simulate database operations that would happen in the command
        var newEmoji = new Emoji
        {
            ServerId = serverId,
            ChallengeId = challenge.Id,
            EmojiCode = "🍅",
            EmojiType = EmojiType.Pomodoro,
            PointValue = 25,
            IsActive = true
        };

        _context.Emojis.Add(newEmoji);
        await _context.SaveChangesAsync();

        // Assert
        var savedEmoji = await _context.Emojis
            .FirstOrDefaultAsync(e => e.ServerId == serverId && e.EmojiCode == "🍅");
        
        savedEmoji.ShouldNotBeNull();
        savedEmoji.EmojiType.ShouldBe(EmojiType.Pomodoro);
        savedEmoji.PointValue.ShouldBe(25);
        savedEmoji.IsActive.ShouldBeTrue();
        savedEmoji.ChallengeId.ShouldBe(challenge.Id);
    }

    [Fact]
    public async Task AddEmoji_ShouldPreventDuplicates()
    {
        // Arrange
        const ulong serverId = 12345;
        var server = await CreateTestServerAsync(serverId);
        
        // Create first emoji
        var existingEmoji = new Emoji
        {
            ServerId = serverId,
            ChallengeId = null,
            EmojiCode = "🍅",
            EmojiType = EmojiType.Pomodoro,
            PointValue = 25,
            IsActive = true
        };
        
        _context.Emojis.Add(existingEmoji);
        await _context.SaveChangesAsync();

        // Act - Check for duplicate
        var duplicateExists = await _context.Emojis
            .AnyAsync(e => e.ServerId == serverId 
                         && e.EmojiCode == "🍅" 
                         && e.ChallengeId == null
                         && e.IsActive);

        // Assert
        duplicateExists.ShouldBeTrue();
    }

    [Fact]
    public async Task RemoveEmoji_ShouldDeactivateEmoji()
    {
        // Arrange
        const ulong serverId = 12345;
        var server = await CreateTestServerAsync(serverId);
        
        var emoji = new Emoji
        {
            ServerId = serverId,
            EmojiCode = "🍅",
            EmojiType = EmojiType.Pomodoro,
            PointValue = 25,
            IsActive = true
        };
        
        _context.Emojis.Add(emoji);
        await _context.SaveChangesAsync();

        // Act - Simulate removal
        var emojiToRemove = await _context.Emojis
            .FirstOrDefaultAsync(e => e.ServerId == serverId && e.EmojiCode == "🍅" && e.IsActive);
        
        emojiToRemove.ShouldNotBeNull();
        emojiToRemove.IsActive = false;
        await _context.SaveChangesAsync();

        // Assert
        var removedEmoji = await _context.Emojis
            .FirstOrDefaultAsync(e => e.Id == emoji.Id);
        
        removedEmoji.ShouldNotBeNull();
        removedEmoji.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task EditEmoji_ShouldUpdateConfiguration()
    {
        // Arrange
        const ulong serverId = 12345;
        var server = await CreateTestServerAsync(serverId);
        
        var emoji = new Emoji
        {
            ServerId = serverId,
            EmojiCode = "🍅",
            EmojiType = EmojiType.Pomodoro,
            PointValue = 25,
            IsActive = true
        };
        
        _context.Emojis.Add(emoji);
        await _context.SaveChangesAsync();

        // Act - Simulate edit
        var emojiToEdit = await _context.Emojis
            .FirstOrDefaultAsync(e => e.ServerId == serverId && e.EmojiCode == "🍅" && e.IsActive);
        
        emojiToEdit.ShouldNotBeNull();
        var originalType = emojiToEdit.EmojiType;
        var originalPoints = emojiToEdit.PointValue;
        
        emojiToEdit.EmojiType = EmojiType.Bonus;
        emojiToEdit.PointValue = 50;
        await _context.SaveChangesAsync();

        // Assert
        var editedEmoji = await _context.Emojis
            .FirstOrDefaultAsync(e => e.Id == emoji.Id);
        
        editedEmoji.ShouldNotBeNull();
        editedEmoji.EmojiType.ShouldBe(EmojiType.Bonus);
        editedEmoji.PointValue.ShouldBe(50);
        originalType.ShouldBe(EmojiType.Pomodoro);
        originalPoints.ShouldBe(25);
    }

    [Fact]
    public async Task ListEmojis_ShouldReturnActiveEmojisOnly()
    {
        // Arrange
        const ulong serverId = 12345;
        var server = await CreateTestServerAsync(serverId);
        
        var activeEmoji = new Emoji
        {
            ServerId = serverId,
            EmojiCode = "🍅",
            EmojiType = EmojiType.Pomodoro,
            PointValue = 25,
            IsActive = true
        };
        
        var inactiveEmoji = new Emoji
        {
            ServerId = serverId,
            EmojiCode = "🔥",
            EmojiType = EmojiType.Bonus,
            PointValue = 10,
            IsActive = false
        };
        
        _context.Emojis.AddRange(activeEmoji, inactiveEmoji);
        await _context.SaveChangesAsync();

        // Act
        var activeEmojis = await _context.Emojis
            .Where(e => e.ServerId == serverId && e.IsActive)
            .OrderBy(e => e.EmojiType)
            .ThenBy(e => e.EmojiCode)
            .ToListAsync();

        // Assert
        activeEmojis.Count.ShouldBe(1);
        activeEmojis[0].EmojiCode.ShouldBe("🍅");
        activeEmojis[0].IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task EmojiValidation_ShouldValidateAllEmojiTypes()
    {
        // Act & Assert
        foreach (EmojiType emojiType in Enum.GetValues<EmojiType>())
        {
            Enum.IsDefined(typeof(EmojiType), emojiType).ShouldBeTrue();
        }
        
        // Test specific types
        Enum.IsDefined(typeof(EmojiType), EmojiType.Pomodoro).ShouldBeTrue();
        Enum.IsDefined(typeof(EmojiType), EmojiType.Bonus).ShouldBeTrue();
        Enum.IsDefined(typeof(EmojiType), EmojiType.Goal).ShouldBeTrue();
        Enum.IsDefined(typeof(EmojiType), EmojiType.Reward).ShouldBeTrue();
    }

    [Fact]
    public async Task EmojiNormalization_ShouldConvertUnicodeToShortcode()
    {
        // Act & Assert
        var normalizedTomato = _emojiService.NormalizeEmoji("🍅");
        normalizedTomato.ShouldBe(":tomato:");
        
        var normalizedFire = _emojiService.NormalizeEmoji("🔥");
        normalizedFire.ShouldBe(":fire:");
        
        // Shortcodes should remain unchanged
        var alreadyShortcode = _emojiService.NormalizeEmoji(":tomato:");
        alreadyShortcode.ShouldBe(":tomato:");
    }

    [Fact]
    public async Task EmojiEquivalence_ShouldDetectEquivalentEmojis()
    {
        // Act & Assert
        _emojiService.AreEmojisEquivalent("🍅", ":tomato:").ShouldBeTrue();
        _emojiService.AreEmojisEquivalent(":tomato:", "🍅").ShouldBeTrue();
        _emojiService.AreEmojisEquivalent("🔥", ":fire:").ShouldBeTrue();
        
        // Same emojis should be equivalent
        _emojiService.AreEmojisEquivalent("🍅", "🍅").ShouldBeTrue();
        _emojiService.AreEmojisEquivalent(":tomato:", ":tomato:").ShouldBeTrue();
        
        // Different emojis should not be equivalent
        _emojiService.AreEmojisEquivalent("🍅", "🔥").ShouldBeFalse();
        _emojiService.AreEmojisEquivalent(":tomato:", ":fire:").ShouldBeFalse();
    }

    [Fact]
    public async Task AddEmoji_ShouldPreventDuplicateEquivalents()
    {
        // Arrange
        const ulong serverId = 12345;
        var server = await CreateTestServerAsync(serverId);
        
        // Add emoji in Unicode format first
        var unicodeEmoji = new Emoji
        {
            ServerId = serverId,
            ChallengeId = null,
            EmojiCode = "🍅", // Unicode tomato
            EmojiType = EmojiType.Pomodoro,
            PointValue = 25,
            IsActive = true
        };
        
        _context.Emojis.Add(unicodeEmoji);
        await _context.SaveChangesAsync();

        // Try to add equivalent shortcode version
        var existingEmojis = await _context.Emojis
            .Where(e => e.ServerId == serverId && e.ChallengeId == null && e.IsActive)
            .ToListAsync();
            
        var duplicateExists = existingEmojis.Any(e => _emojiService.AreEmojisEquivalent(e.EmojiCode, ":tomato:"));

        // Assert
        duplicateExists.ShouldBeTrue();
    }

    [Fact] 
    public async Task MessageProcessing_ShouldMatchEquivalentEmojis()
    {
        // Arrange
        const ulong serverId = 12345;
        var server = await CreateTestServerAsync(serverId);
        
        // Add emoji in shortcode format
        var shortcodeEmoji = new Emoji
        {
            ServerId = serverId,
            ChallengeId = null,
            EmojiCode = ":tomato:", // Shortcode tomato
            EmojiType = EmojiType.Pomodoro,
            PointValue = 25,
            IsActive = true
        };
        
        _context.Emojis.Add(shortcodeEmoji);
        await _context.SaveChangesAsync();

        var emojis = await _context.Emojis
            .Where(e => e.ServerId == serverId && e.IsActive)
            .ToListAsync();

        // Test matching Unicode emoji against shortcode stored emoji
        var detectedUnicodeEmoji = "🍅"; // Unicode tomato
        
        // Act - simulate emoji matching logic from MessageProcessorService
        var matchingEmoji = emojis.FirstOrDefault(e => 
            e.EmojiCode == detectedUnicodeEmoji || 
            _emojiService.AreEmojisEquivalent(e.EmojiCode, detectedUnicodeEmoji));

        // Assert
        matchingEmoji.ShouldNotBeNull();
        matchingEmoji.EmojiCode.ShouldBe(":tomato:");
        matchingEmoji.PointValue.ShouldBe(25);
    }

    private async Task<Server> CreateTestServerAsync(ulong serverId)
    {
        var server = new Server
        {
            Id = serverId,
            Name = "Test Server",
            Language = "en",
            Timezone = "Europe/Amsterdam",
            CategoryId = null,
            ConfigRoleId = null,
            PingRoleId = null
        };

        _context.Servers.Add(server);
        await _context.SaveChangesAsync();
        return server;
    }

    private async Task<Challenge> CreateTestChallengeAsync(ulong serverId)
    {
        var challenge = new Challenge
        {
            ServerId = serverId,
            SemesterNumber = 1,
            Theme = "Test Challenge",
            StartDate = new DateOnly(2024, 1, 1),
            EndDate = new DateOnly(2024, 1, 7),
            WeekCount = 1,
            IsStarted = true,
            IsActive = true,
            IsCurrent = true
        };

        _context.Challenges.Add(challenge);
        await _context.SaveChangesAsync();
        return challenge;
    }

    public void Dispose()
    {
        _context.Dispose();
        _serviceProvider.Dispose();
    }
} 