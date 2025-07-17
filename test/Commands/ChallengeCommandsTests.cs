using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using PomoChallengeCounter.Data;
using PomoChallengeCounter.Models;
using PomoChallengeCounter.Services;

namespace PomoChallengeCounter.Tests.Commands;

public class ChallengeCommandsTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly PomoChallengeDbContext _context;
    private readonly IChallengeService _challengeService;
    private readonly LocalizationService _localizationService;

    public ChallengeCommandsTests()
    {
        var services = new ServiceCollection();
        
        // Add in-memory database
        services.AddDbContext<PomoChallengeDbContext>(options =>
            options.UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}"));
            
        // Add required services
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<ITimeProvider, MockTimeProvider>();
        services.AddSingleton<NetCord.Gateway.GatewayClient>(provider => null);
        services.AddScoped<IChallengeService, ChallengeService>();
        services.AddLogging();
        
        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<PomoChallengeDbContext>();
        _challengeService = _serviceProvider.GetRequiredService<IChallengeService>();
        _localizationService = _serviceProvider.GetRequiredService<LocalizationService>();
    }

    private async Task<Server> CreateTestServerAsync(ulong serverId = 12345)
    {
        var server = new Server
        {
            Id = serverId,
            Name = "Test Server",
            Language = "en",
            ConfigRoleId = 67890
        };
        
        await _context.Servers.AddAsync(server);
        await _context.SaveChangesAsync();
        return server;
    }

    [Fact]
    public async Task ChallengeService_CreateChallenge_ShouldSucceedWithValidData()
    {
        // Arrange
        await CreateTestServerAsync();
        var semester = 3;
        var theme = "Space Exploration";
        var startDate = new DateOnly(2024, 1, 8); // Monday
        var endDate = new DateOnly(2024, 3, 31);   // Sunday
        var weeks = 12;

        // Act
        var result = await _challengeService.CreateChallengeAsync(12345, semester, theme, startDate, endDate, weeks);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Challenge.ShouldNotBeNull();
        result.Challenge.SemesterNumber.ShouldBe(semester);
        result.Challenge.Theme.ShouldBe(theme);
        result.Challenge.StartDate.ShouldBe(startDate);
        result.Challenge.EndDate.ShouldBe(endDate);
        result.Challenge.WeekCount.ShouldBe(weeks);
        result.Challenge.IsStarted.ShouldBeFalse();
        result.Challenge.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task ChallengeService_CreateChallenge_ShouldFailWithInvalidDates()
    {
        // Arrange
        await CreateTestServerAsync();
        var semester = 3;
        var theme = "Test Theme";
        var startDate = new DateOnly(2024, 1, 9); // Tuesday (should be Monday)
        var endDate = new DateOnly(2024, 3, 30);   // Saturday (should be Sunday)
        var weeks = 12;

        // Act
        var result = await _challengeService.CreateChallengeAsync(12345, semester, theme, startDate, endDate, weeks);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("Start date must be a Monday");
        result.ErrorMessage.ShouldContain("End date must be a Sunday");
    }

    [Fact]
    public async Task ChallengeService_CreateChallenge_ShouldFailWithMismatchedWeekCount()
    {
        // Arrange
        await CreateTestServerAsync();
        var semester = 3;
        var theme = "Test Theme";
        var startDate = new DateOnly(2024, 1, 8); // Monday
        var endDate = new DateOnly(2024, 3, 31);   // Sunday
        var weeks = 15; // Wrong! Should be 12 weeks

        // Act
        var result = await _challengeService.CreateChallengeAsync(12345, semester, theme, startDate, endDate, weeks);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("Week count (15) does not match date range (12 weeks");
    }

    [Fact]
    public async Task ChallengeService_StartChallenge_ShouldSucceedWithValidChallenge()
    {
        // Arrange
        await CreateTestServerAsync();
        var createResult = await _challengeService.CreateChallengeAsync(
            12345, 3, "Test Theme", 
            new DateOnly(2024, 1, 8), new DateOnly(2024, 3, 31), 12);
        createResult.IsSuccess.ShouldBeTrue();

        // Act
        var startResult = await _challengeService.StartChallengeAsync(createResult.Challenge!.Id);

        // Assert
        startResult.IsSuccess.ShouldBeTrue();
        startResult.Challenge.ShouldNotBeNull();
        startResult.Challenge.IsStarted.ShouldBeTrue();
        startResult.Challenge.IsActive.ShouldBeTrue();
        startResult.Challenge.IsCurrent.ShouldBeTrue();

        // Verify weeks were created
        var weeks = await _context.Weeks
            .Where(w => w.ChallengeId == createResult.Challenge.Id)
            .ToListAsync();
        weeks.Count.ShouldBe(2); // Week 0 and Week 1
        weeks.Any(w => w.WeekNumber == 0).ShouldBeTrue();
        weeks.Any(w => w.WeekNumber == 1).ShouldBeTrue();
    }

    [Fact]
    public async Task ChallengeService_StartChallenge_ShouldFailIfAlreadyStarted()
    {
        // Arrange
        await CreateTestServerAsync();
        var createResult = await _challengeService.CreateChallengeAsync(
            12345, 3, "Test Theme", 
            new DateOnly(2024, 1, 8), new DateOnly(2024, 3, 31), 12);
        await _challengeService.StartChallengeAsync(createResult.Challenge!.Id);

        // Act
        var secondStartResult = await _challengeService.StartChallengeAsync(createResult.Challenge.Id);

        // Assert
        secondStartResult.IsSuccess.ShouldBeFalse();
        secondStartResult.ErrorMessage.ShouldBe("Challenge is already started");
    }

    [Fact]
    public async Task ChallengeService_StopChallenge_ShouldSucceedWithStartedChallenge()
    {
        // Arrange
        await CreateTestServerAsync();
        var createResult = await _challengeService.CreateChallengeAsync(
            12345, 3, "Test Theme", 
            new DateOnly(2024, 1, 8), new DateOnly(2024, 3, 31), 12);
        await _challengeService.StartChallengeAsync(createResult.Challenge!.Id);

        // Act
        var stopResult = await _challengeService.StopChallengeAsync(createResult.Challenge.Id);

        // Assert
        stopResult.IsSuccess.ShouldBeTrue();
        stopResult.Challenge.ShouldNotBeNull();
        stopResult.Challenge.IsActive.ShouldBeFalse();
        stopResult.Challenge.IsCurrent.ShouldBeFalse();
        stopResult.Challenge.IsStarted.ShouldBeTrue(); // Remains true
    }

    [Fact]
    public async Task ChallengeService_DeactivateChallenge_ShouldSucceedAndStopMessageProcessing()
    {
        // Arrange
        await CreateTestServerAsync();
        var createResult = await _challengeService.CreateChallengeAsync(
            12345, 3, "Test Theme", 
            new DateOnly(2024, 1, 8), new DateOnly(2024, 3, 31), 12);
        await _challengeService.StartChallengeAsync(createResult.Challenge!.Id);

        // Act
        var deactivateResult = await _challengeService.DeactivateChallengeAsync(createResult.Challenge.Id);

        // Assert
        deactivateResult.IsSuccess.ShouldBeTrue();
        deactivateResult.Challenge.ShouldNotBeNull();
        deactivateResult.Challenge.IsActive.ShouldBeFalse();
        // IsCurrent and IsStarted remain unchanged by deactivation
    }

    [Fact]
    public async Task ChallengeService_CreateDuplicateSemester_ShouldFail()
    {
        // Arrange
        await CreateTestServerAsync();
        await _challengeService.CreateChallengeAsync(
            12345, 3, "First Theme", 
            new DateOnly(2024, 1, 8), new DateOnly(2024, 3, 31), 12);

        // Act
        var secondResult = await _challengeService.CreateChallengeAsync(
            12345, 3, "Second Theme", 
            new DateOnly(2024, 4, 8), new DateOnly(2024, 6, 30), 12);

        // Assert
        secondResult.IsSuccess.ShouldBeFalse();
        secondResult.ErrorMessage.ShouldBe("Challenge for semester 3 already exists");
    }

    [Theory]
    [InlineData(0)] // Too low
    [InlineData(6)] // Too high
    public async Task ChallengeService_InvalidSemesterNumber_ShouldFail(int semester)
    {
        // Arrange
        await CreateTestServerAsync();

        // Act
        var result = await _challengeService.CreateChallengeAsync(
            12345, semester, "Test Theme", 
            new DateOnly(2024, 1, 8), new DateOnly(2024, 3, 31), 12);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("Semester number must be between 1-5");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task ChallengeService_InvalidTheme_ShouldFail(string theme)
    {
        // Arrange
        await CreateTestServerAsync();

        // Act
        var result = await _challengeService.CreateChallengeAsync(
            12345, 3, theme, 
            new DateOnly(2024, 1, 8), new DateOnly(2024, 3, 31), 12);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("Theme is required");
    }

    [Fact]
    public async Task ChallengeService_GetCurrentChallenge_ShouldReturnActiveChallenge()
    {
        // Arrange
        await CreateTestServerAsync();
        var createResult = await _challengeService.CreateChallengeAsync(
            12345, 3, "Test Theme", 
            new DateOnly(2024, 1, 8), new DateOnly(2024, 3, 31), 12);
        await _challengeService.StartChallengeAsync(createResult.Challenge!.Id);

        // Act
        var currentChallenge = await _challengeService.GetCurrentChallengeAsync(12345);

        // Assert
        currentChallenge.ShouldNotBeNull();
        currentChallenge.Id.ShouldBe(createResult.Challenge.Id);
        currentChallenge.IsCurrent.ShouldBeTrue();
    }

    [Fact]
    public async Task ChallengeService_GetServerChallenges_ShouldReturnAllChallenges()
    {
        // Arrange
        await CreateTestServerAsync();
        await _challengeService.CreateChallengeAsync(
            12345, 1, "Theme 1", 
            new DateOnly(2024, 1, 8), new DateOnly(2024, 3, 31), 12);
        await _challengeService.CreateChallengeAsync(
            12345, 2, "Theme 2", 
            new DateOnly(2024, 4, 8), new DateOnly(2024, 6, 30), 12);

        // Act
        var challenges = await _challengeService.GetServerChallengesAsync(12345);

        // Assert
        challenges.Count.ShouldBe(2);
        challenges[0].SemesterNumber.ShouldBe(2); // Ordered by semester descending
        challenges[1].SemesterNumber.ShouldBe(1);
    }

    public void Dispose()
    {
        _context?.Dispose();
        _serviceProvider?.Dispose();
    }
} 