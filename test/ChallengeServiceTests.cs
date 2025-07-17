using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PomoChallengeCounter.Data;
using PomoChallengeCounter.Models;
using PomoChallengeCounter.Services;
using Shouldly;

namespace PomoChallengeCounter.Tests;

public class ChallengeServiceTests : IDisposable
{
    private readonly PomoChallengeDbContext _context;
    private readonly ChallengeService _challengeService;
    
    private Server _testServer = null!;

    public ChallengeServiceTests()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<PomoChallengeDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new PomoChallengeDbContext(options);

        // Setup mocks
        var mockTimeProvider = new MockTimeProvider();
        mockTimeProvider.SetDate(new DateOnly(2024, 3, 11)); // Monday
        var mockLogger = new Mock<ILogger<ChallengeService>>();
        
        var mockServiceProvider = new Mock<IServiceProvider>();
        
        _challengeService = new ChallengeService(_context, mockTimeProvider, null, mockServiceProvider.Object, mockLogger.Object);

        // Setup test data
        SetupTestData();
    }

    private void SetupTestData()
    {
        _testServer = new Server
        {
            Id = 123456789,
            Name = "Test Server",
            Language = "en"
        };

        _context.Servers.Add(_testServer);
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async Task CreateChallengeAsync_WithValidParameters_ShouldCreateChallenge()
    {
        // Arrange
        var semesterNumber = 3;
        var theme = "Space Exploration";
        var startDate = new DateOnly(2024, 3, 18); // Monday
        var endDate = new DateOnly(2024, 6, 9); // Sunday
        var weekCount = 12;

        // Act
        var result = await _challengeService.CreateChallengeAsync(_testServer.Id, semesterNumber, theme, startDate, endDate, weekCount);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Challenge.ShouldNotBeNull();
        result.Challenge!.ServerId.ShouldBe(_testServer.Id);
        result.Challenge.SemesterNumber.ShouldBe(semesterNumber);
        result.Challenge.Theme.ShouldBe(theme);
        result.Challenge.StartDate.ShouldBe(startDate);
        result.Challenge.EndDate.ShouldBe(endDate);
        result.Challenge.WeekCount.ShouldBe(weekCount);
        result.Challenge.IsStarted.ShouldBeFalse();
        result.Challenge.IsActive.ShouldBeFalse();
        result.Challenge.IsCurrent.ShouldBeFalse();

        // Verify in database
        var dbChallenge = await _context.Challenges.FirstAsync(c => c.Id == result.Challenge.Id);
        dbChallenge.ShouldNotBeNull();
        dbChallenge.Theme.ShouldBe(theme);
    }

    [Fact]
    public async Task CreateChallengeAsync_WithInvalidSemester_ShouldFail()
    {
        // Arrange
        var semesterNumber = 6; // Invalid
        var theme = "Test Theme";
        var startDate = new DateOnly(2024, 3, 18);
        var endDate = new DateOnly(2024, 6, 9);
        var weekCount = 12;

        // Act
        var result = await _challengeService.CreateChallengeAsync(_testServer.Id, semesterNumber, theme, startDate, endDate, weekCount);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("Semester number must be between 1-5");
    }

    [Fact]
    public async Task CreateChallengeAsync_WithInvalidStartDate_ShouldFail()
    {
        // Arrange
        var startDate = new DateOnly(2024, 3, 19); // Tuesday, not Monday

        // Act
        var result = await _challengeService.CreateChallengeAsync(_testServer.Id, 3, "Theme", startDate, new DateOnly(2024, 6, 9), 12);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("Start date must be a Monday");
    }

    [Fact]
    public async Task CreateChallengeAsync_WithInvalidEndDate_ShouldFail()
    {
        // Arrange
        var endDate = new DateOnly(2024, 6, 10); // Monday, not Sunday

        // Act
        var result = await _challengeService.CreateChallengeAsync(_testServer.Id, 3, "Theme", new DateOnly(2024, 3, 18), endDate, 12);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("End date must be a Sunday");
    }

    [Fact]
    public async Task CreateChallengeAsync_WithMismatchedWeekCount_ShouldFail()
    {
        // Arrange
        var startDate = new DateOnly(2024, 3, 18); // Monday
        var endDate = new DateOnly(2024, 6, 9); // Sunday - actually 12 weeks
        var weekCount = 10; // Wrong count

        // Act
        var result = await _challengeService.CreateChallengeAsync(_testServer.Id, 3, "Theme", startDate, endDate, weekCount);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("Week count (10) does not match date range (12 weeks");
    }

    [Fact]
    public async Task CreateChallengeAsync_WithPastStartDate_ShouldFail()
    {
        // Arrange
        var pastDate = new DateOnly(2024, 3, 4); // Past Monday

        // Act
        var result = await _challengeService.CreateChallengeAsync(_testServer.Id, 3, "Theme", pastDate, new DateOnly(2024, 6, 9), 14);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("Start date cannot be in the past");
    }

    [Fact]
    public async Task CreateChallengeAsync_WithDuplicateSemester_ShouldFail()
    {
        // Arrange
        var existingChallenge = new Challenge
        {
            ServerId = _testServer.Id,
            SemesterNumber = 3,
            Theme = "Existing Challenge",
            StartDate = new DateOnly(2024, 3, 18),
            EndDate = new DateOnly(2024, 6, 9),
            WeekCount = 12
        };
        _context.Challenges.Add(existingChallenge);
        await _context.SaveChangesAsync();

        // Act
        var result = await _challengeService.CreateChallengeAsync(_testServer.Id, 3, "New Challenge", new DateOnly(2024, 9, 2), new DateOnly(2024, 12, 22), 16);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("Challenge for semester 3 already exists");
    }

    [Fact]
    public async Task CreateChallengeAsync_WithNonExistentServer_ShouldFail()
    {
        // Act
        var result = await _challengeService.CreateChallengeAsync(999999, 3, "Theme", new DateOnly(2024, 3, 18), new DateOnly(2024, 6, 9), 12);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldBe("Server not found");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task CreateChallengeAsync_WithEmptyTheme_ShouldFail(string theme)
    {
        // Act
        var result = await _challengeService.CreateChallengeAsync(_testServer.Id, 3, theme, new DateOnly(2024, 3, 18), new DateOnly(2024, 6, 9), 12);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("Theme is required");
    }

    [Fact]
    public async Task CreateChallengeAsync_WithTooLongTheme_ShouldFail()
    {
        // Arrange
        var longTheme = new string('A', 256); // 256 characters

        // Act
        var result = await _challengeService.CreateChallengeAsync(_testServer.Id, 3, longTheme, new DateOnly(2024, 3, 18), new DateOnly(2024, 6, 9), 12);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("Theme must be 255 characters or less");
    }

    [Fact]
    public async Task StartChallengeAsync_WithValidChallenge_ShouldStartChallenge()
    {
        // Arrange
        var challenge = new Challenge
        {
            ServerId = _testServer.Id,
            SemesterNumber = 3,
            Theme = "Test Challenge",
            StartDate = new DateOnly(2024, 3, 18),
            EndDate = new DateOnly(2024, 6, 9),
            WeekCount = 12,
            IsStarted = false,
            IsActive = false,
            IsCurrent = false
        };
        _context.Challenges.Add(challenge);
        await _context.SaveChangesAsync();

        // Act
        var result = await _challengeService.StartChallengeAsync(challenge.Id);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Challenge.ShouldNotBeNull();
        result.Challenge!.IsStarted.ShouldBeTrue();
        result.Challenge.IsActive.ShouldBeTrue();
        result.Challenge.IsCurrent.ShouldBeTrue();

        // Verify weeks were created
        var weeks = await _context.Weeks.Where(w => w.ChallengeId == challenge.Id).ToListAsync();
        weeks.Count.ShouldBe(2);
        weeks.ShouldContain(w => w.WeekNumber == 0); // Goal week
        weeks.ShouldContain(w => w.WeekNumber == 1); // First week
    }

    [Fact]
    public async Task StartChallengeAsync_WithAlreadyStartedChallenge_ShouldFail()
    {
        // Arrange
        var challenge = new Challenge
        {
            ServerId = _testServer.Id,
            SemesterNumber = 3,
            Theme = "Test Challenge",
            StartDate = new DateOnly(2024, 3, 18),
            EndDate = new DateOnly(2024, 6, 9),
            WeekCount = 12,
            IsStarted = true
        };
        _context.Challenges.Add(challenge);
        await _context.SaveChangesAsync();

        // Act
        var result = await _challengeService.StartChallengeAsync(challenge.Id);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldBe("Challenge is already started");
    }

    [Fact]
    public async Task StartChallengeAsync_ShouldDeactivateOtherCurrentChallenges()
    {
        // Arrange
        var oldChallenge = new Challenge
        {
            ServerId = _testServer.Id,
            SemesterNumber = 2,
            Theme = "Old Challenge",
            StartDate = new DateOnly(2024, 1, 8),
            EndDate = new DateOnly(2024, 3, 10),
            WeekCount = 9,
            IsStarted = true,
            IsActive = true,
            IsCurrent = true
        };
        
        var newChallenge = new Challenge
        {
            ServerId = _testServer.Id,
            SemesterNumber = 3,
            Theme = "New Challenge",
            StartDate = new DateOnly(2024, 3, 18),
            EndDate = new DateOnly(2024, 6, 9),
            WeekCount = 12,
            IsStarted = false,
            IsActive = false,
            IsCurrent = false
        };
        
        _context.Challenges.AddRange(oldChallenge, newChallenge);
        await _context.SaveChangesAsync();

        // Act
        var result = await _challengeService.StartChallengeAsync(newChallenge.Id);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        
        // Verify old challenge was deactivated
        var updatedOldChallenge = await _context.Challenges.FirstAsync(c => c.Id == oldChallenge.Id);
        updatedOldChallenge.IsCurrent.ShouldBeFalse();
        updatedOldChallenge.IsActive.ShouldBeFalse();
        
        // Verify new challenge is current
        var updatedNewChallenge = await _context.Challenges.FirstAsync(c => c.Id == newChallenge.Id);
        updatedNewChallenge.IsCurrent.ShouldBeTrue();
        updatedNewChallenge.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task StartChallengeAsync_WithNonExistentChallenge_ShouldFail()
    {
        // Act
        var result = await _challengeService.StartChallengeAsync(999);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldBe("Challenge not found");
    }

    [Fact]
    public async Task StopChallengeAsync_WithStartedChallenge_ShouldStopChallenge()
    {
        // Arrange
        var challenge = new Challenge
        {
            ServerId = _testServer.Id,
            SemesterNumber = 3,
            Theme = "Test Challenge",
            StartDate = new DateOnly(2024, 3, 18),
            EndDate = new DateOnly(2024, 6, 9),
            WeekCount = 12,
            IsStarted = true,
            IsActive = true,
            IsCurrent = true
        };
        _context.Challenges.Add(challenge);
        await _context.SaveChangesAsync();

        // Act
        var result = await _challengeService.StopChallengeAsync(challenge.Id);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Challenge.ShouldNotBeNull();
        result.Challenge!.IsActive.ShouldBeFalse();
        result.Challenge.IsCurrent.ShouldBeFalse();
        result.Challenge.IsStarted.ShouldBeTrue(); // Should remain started
    }

    [Fact]
    public async Task StopChallengeAsync_WithNotStartedChallenge_ShouldFail()
    {
        // Arrange
        var challenge = new Challenge
        {
            ServerId = _testServer.Id,
            SemesterNumber = 3,
            Theme = "Test Challenge",
            StartDate = new DateOnly(2024, 3, 18),
            EndDate = new DateOnly(2024, 6, 9),
            WeekCount = 12,
            IsStarted = false
        };
        _context.Challenges.Add(challenge);
        await _context.SaveChangesAsync();

        // Act
        var result = await _challengeService.StopChallengeAsync(challenge.Id);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldBe("Challenge is not started");
    }

    [Fact]
    public async Task DeactivateChallengeAsync_WithValidChallenge_ShouldDeactivate()
    {
        // Arrange
        var challenge = new Challenge
        {
            ServerId = _testServer.Id,
            SemesterNumber = 3,
            Theme = "Test Challenge",
            StartDate = new DateOnly(2024, 3, 18),
            EndDate = new DateOnly(2024, 6, 9),
            WeekCount = 12,
            IsStarted = true,
            IsActive = true,
            IsCurrent = true
        };
        _context.Challenges.Add(challenge);
        await _context.SaveChangesAsync();

        // Act
        var result = await _challengeService.DeactivateChallengeAsync(challenge.Id);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Challenge.ShouldNotBeNull();
        result.Challenge!.IsActive.ShouldBeFalse();
        result.Challenge.IsCurrent.ShouldBeTrue(); // Should remain current (unlike stop)
        result.Challenge.IsStarted.ShouldBeTrue(); // Should remain started
    }

    [Fact]
    public async Task GetCurrentChallengeAsync_WithCurrentChallenge_ShouldReturnChallenge()
    {
        // Arrange
        var challenge = new Challenge
        {
            ServerId = _testServer.Id,
            SemesterNumber = 3,
            Theme = "Current Challenge",
            StartDate = new DateOnly(2024, 3, 18),
            EndDate = new DateOnly(2024, 6, 9),
            WeekCount = 12,
            IsStarted = true,
            IsActive = true,
            IsCurrent = true
        };
        _context.Challenges.Add(challenge);
        await _context.SaveChangesAsync();

        // Act
        var result = await _challengeService.GetCurrentChallengeAsync(_testServer.Id);

        // Assert
        result.ShouldNotBeNull();
        result!.Id.ShouldBe(challenge.Id);
        result.Theme.ShouldBe("Current Challenge");
        result.IsCurrent.ShouldBeTrue();
    }

    [Fact]
    public async Task GetCurrentChallengeAsync_WithNoCurrentChallenge_ShouldReturnNull()
    {
        // Act
        var result = await _challengeService.GetCurrentChallengeAsync(_testServer.Id);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetChallengeAsync_WithValidId_ShouldReturnChallenge()
    {
        // Arrange
        var challenge = new Challenge
        {
            ServerId = _testServer.Id,
            SemesterNumber = 3,
            Theme = "Test Challenge",
            StartDate = new DateOnly(2024, 3, 18),
            EndDate = new DateOnly(2024, 6, 9),
            WeekCount = 12
        };
        _context.Challenges.Add(challenge);
        await _context.SaveChangesAsync();

        // Act
        var result = await _challengeService.GetChallengeAsync(challenge.Id);

        // Assert
        result.ShouldNotBeNull();
        result!.Id.ShouldBe(challenge.Id);
        result.Theme.ShouldBe("Test Challenge");
    }

    [Fact]
    public async Task GetChallengeAsync_WithInvalidId_ShouldReturnNull()
    {
        // Act
        var result = await _challengeService.GetChallengeAsync(999);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetServerChallengesAsync_ShouldReturnOrderedChallenges()
    {
        // Arrange
        var challenges = new[]
        {
            new Challenge { ServerId = _testServer.Id, SemesterNumber = 1, Theme = "First", StartDate = new DateOnly(2024, 1, 8), EndDate = new DateOnly(2024, 3, 31), WeekCount = 12 },
            new Challenge { ServerId = _testServer.Id, SemesterNumber = 3, Theme = "Third", StartDate = new DateOnly(2024, 6, 3), EndDate = new DateOnly(2024, 8, 25), WeekCount = 12 },
            new Challenge { ServerId = _testServer.Id, SemesterNumber = 2, Theme = "Second", StartDate = new DateOnly(2024, 3, 11), EndDate = new DateOnly(2024, 6, 2), WeekCount = 12 }
        };
        _context.Challenges.AddRange(challenges);
        await _context.SaveChangesAsync();

        // Act
        var result = await _challengeService.GetServerChallengesAsync(_testServer.Id);

        // Assert
        result.Count.ShouldBe(3);
        result[0].SemesterNumber.ShouldBe(3); // Ordered by semester desc
        result[1].SemesterNumber.ShouldBe(2);
        result[2].SemesterNumber.ShouldBe(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void ValidateChallengeParameters_WithInvalidSemester_ShouldReturnErrors(int semester)
    {
        // Act
        var result = _challengeService.ValidateChallengeParameters(semester, "Theme", new DateOnly(2024, 3, 18), new DateOnly(2024, 6, 9), 12);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Contains("Semester number must be between 1-5"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ValidateChallengeParameters_WithValidSemester_ShouldNotHaveSemesterError(int semester)
    {
        // Act
        var result = _challengeService.ValidateChallengeParameters(semester, "Theme", new DateOnly(2024, 3, 18), new DateOnly(2024, 6, 9), 12);

        // Assert
        result.Errors.ShouldNotContain(e => e.Contains("Semester number must be between 1-5"));
    }

    [Fact]
    public void ValidateChallengeParameters_WithValidParameters_ShouldBeValid()
    {
        // Act
        var result = _challengeService.ValidateChallengeParameters(3, "Space Exploration", new DateOnly(2024, 3, 18), new DateOnly(2024, 6, 9), 12);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void ValidateChallengeParameters_WithTooLongWeekCount_ShouldReturnError()
    {
        // Act
        var result = _challengeService.ValidateChallengeParameters(3, "Theme", new DateOnly(2024, 3, 18), new DateOnly(2025, 3, 16), 53);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Contains("Challenge cannot be longer than 52 weeks"));
    }

    [Fact]
    public void ValidateChallengeParameters_WithZeroWeekCount_ShouldReturnError()
    {
        // Act
        var result = _challengeService.ValidateChallengeParameters(3, "Theme", new DateOnly(2024, 3, 18), new DateOnly(2024, 3, 24), 0);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Contains("Challenge must be at least 1 week long"));
    }

    [Fact]
    public async Task CreateChallenge_WithInvalidTheme_ShouldFail()
    {
        // Test with null theme to trigger validation
        var result = await _challengeService.CreateChallengeAsync(_testServer.Id, 3, null!, new DateOnly(2024, 3, 18), new DateOnly(2024, 6, 9), 12);

        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("Theme is required");
    }

    [Theory]
    [InlineData("Q3-week1", 3, true, 1)]
    [InlineData("Q5-Week0-Inzet", 5, true, 0)]
    [InlineData("Q1-week0-inzet", 1, true, 0)]  // Specific pattern user mentioned
    [InlineData("q1-week1-inzet", 1, true, 1)]  // Lowercase + week1 with inzet suffix
    [InlineData("q1-week0-inzet", 1, true, 0)]  // Lowercase + week0 with inzet suffix
    [InlineData("Q3-WEEK12", 3, true, 12)]
    [InlineData("q5-week1", 5, true, 1)]
    [InlineData("Q2-week5-Extra-Info", 2, true, 5)]
    [InlineData("Q3-week1", 2, false, 0)] // Wrong semester
    [InlineData("Random-Thread", 3, false, 0)] // Wrong pattern
    [InlineData("Q3-week", 3, false, 0)] // Missing number
    [InlineData("Q3-weekX", 3, false, 0)] // Invalid number
    [InlineData("", 3, false, 0)] // Empty string
    [InlineData("Q3-week-1", 3, false, 0)] // Extra dash before number
    public void ParseThreadName_ShouldParseCorrectly(string threadName, int semesterNumber, bool expectedMatch, int expectedWeekNumber)
    {
        // Arrange & Act - using reflection to access private method
        var method = typeof(ChallengeService).GetMethod("ParseThreadName", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var result = method?.Invoke(null, new object[] { threadName, semesterNumber });
        
        // Assert
        result.ShouldNotBeNull();
        var (isMatch, weekNumber) = ((bool, int))result;
        isMatch.ShouldBe(expectedMatch);
        weekNumber.ShouldBe(expectedWeekNumber);
    }

    [Theory]
    [InlineData("Q3-week1", 3, true)]
    [InlineData("Q5-Week0-Inzet", 5, true)]
    [InlineData("Q1-week0-inzet", 1, true)]  // Specific pattern user mentioned
    [InlineData("q1-week1-inzet", 1, true)]  // Lowercase + week1 with inzet suffix
    [InlineData("q1-week0-inzet", 1, true)]  // Lowercase + week0 with inzet suffix
    [InlineData("Q3-WEEK12", 3, true)]
    [InlineData("q5-week1", 5, true)]
    [InlineData("Q3-week1", 2, false)] // Wrong semester
    [InlineData("Random-Thread", 3, false)] // Wrong pattern
    public void IsValidChallengeThread_ShouldValidateCorrectly(string threadName, int semesterNumber, bool expected)
    {
        // Arrange & Act - using reflection to access private method
        var method = typeof(ChallengeService).GetMethod("IsValidChallengeThread", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var result = method?.Invoke(null, new object[] { threadName, semesterNumber });
        
        // Assert
        result.ShouldNotBeNull();
        ((bool)result).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Q3-week1", 3, 1)]
    [InlineData("Q5-Week0-Inzet", 5, 0)]
    [InlineData("Q1-week0-inzet", 1, 0)]  // Specific pattern user mentioned
    [InlineData("q1-week1-inzet", 1, 1)]  // Lowercase + week1 with inzet suffix
    [InlineData("q1-week0-inzet", 1, 0)]  // Lowercase + week0 with inzet suffix
    [InlineData("Q3-WEEK12", 3, 12)]
    [InlineData("Q3-week1", 2, -1)] // Wrong semester
    [InlineData("Random-Thread", 3, -1)] // Wrong pattern
    public void GetWeekNumberFromThreadName_ShouldExtractCorrectly(string threadName, int semesterNumber, int expected)
    {
        // Arrange & Act - using reflection to access private method
        var method = typeof(ChallengeService).GetMethod("GetWeekNumberFromThreadName", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var result = method?.Invoke(null, new object[] { threadName, semesterNumber });

        // Assert
        result.ShouldNotBeNull();
        ((int)result).ShouldBe(expected);
    }
} 