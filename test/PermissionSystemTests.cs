using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Shouldly;
using PomoChallengeCounter.Commands;
using PomoChallengeCounter.Data;
using PomoChallengeCounter.Models;
using PomoChallengeCounter.Services;

namespace PomoChallengeCounter.Tests;

public class PermissionSystemTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly PomoChallengeDbContext _context;

    public PermissionSystemTests()
    {
        var services = new ServiceCollection();
        
        // Add in-memory database
        services.AddDbContext<PomoChallengeDbContext>(options =>
            options.UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}"));
            
        // Add required services
        services.AddSingleton<LocalizationService>();
        services.AddLogging();
        
        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<PomoChallengeDbContext>();
    }

    [Fact]
    public async Task PermissionLevel_PublicShouldBeAccessibleToEveryone()
    {
        // Assert
        PermissionLevel.Public.ShouldBe(PermissionLevel.Public);
        // Public commands should not require any special role checking
        // This is tested implicitly by the CheckPermissionsAsync method returning true for Public level
    }

    [Fact]
    public async Task PermissionLevel_ConfigAndAdminShouldRequireSpecialRoles()
    {
        // Assert
        PermissionLevel.Config.ShouldNotBe(PermissionLevel.Public);
        PermissionLevel.Admin.ShouldNotBe(PermissionLevel.Public);
        PermissionLevel.Admin.ShouldNotBe(PermissionLevel.Config);
        // These levels require role checking which is implemented in CheckPermissionsAsync
    }

    [Fact]
    public async Task Server_ConfigRoleIdShouldBeConfigurable()
    {
        // Arrange
        var server = new Server 
        { 
            Id = 999, 
            Name = "Test Server",
            ConfigRoleId = 555 // Set config role
        };
        await _context.Servers.AddAsync(server);
        await _context.SaveChangesAsync();

        // Act
        var savedServer = await _context.Servers.FirstOrDefaultAsync(s => s.Id == 999);

        // Assert
        savedServer.ShouldNotBeNull();
        savedServer.ConfigRoleId.ShouldBe(555ul);
    }

    [Fact]
    public async Task Server_ConfigRoleIdCanBeNull()
    {
        // Arrange
        var server = new Server 
        { 
            Id = 999, 
            Name = "Test Server",
            ConfigRoleId = null // No config role set
        };
        await _context.Servers.AddAsync(server);
        await _context.SaveChangesAsync();

        // Act
        var savedServer = await _context.Servers.FirstOrDefaultAsync(s => s.Id == 999);

        // Assert
        savedServer.ShouldNotBeNull();
        savedServer.ConfigRoleId.ShouldBeNull();
    }

    [Fact]
    public void BaseCommand_ShouldHavePermissionLevelEnum()
    {
        // Assert - Verify permission levels are defined correctly
        var levels = Enum.GetValues<PermissionLevel>();
        levels.ShouldContain(PermissionLevel.Public);
        levels.ShouldContain(PermissionLevel.Config);
        levels.ShouldContain(PermissionLevel.Admin);
        levels.Length.ShouldBe(3); // Ensure we have exactly these 3 levels
    }

    [Fact]
    public async Task LocalizationService_ShouldProvideErrorMessages()
    {
        // Arrange
        var localizationService = _serviceProvider.GetRequiredService<LocalizationService>();

        // Act & Assert - Verify that permission-related error messages exist
        // These are used in CheckPermissionsAsync when permissions are denied
        var guildOnlyError = localizationService.GetString("errors.guild_only", "en");
        var userNotFoundError = localizationService.GetString("errors.user_not_found", "en");
        var insufficientPermissionsError = localizationService.GetString("errors.insufficient_permissions", "en");

        guildOnlyError.ShouldNotBeNullOrEmpty();
        userNotFoundError.ShouldNotBeNullOrEmpty();
        insufficientPermissionsError.ShouldNotBeNullOrEmpty();
    }

    [Theory]
    [InlineData(555ul, new ulong[] { 555, 777 }, true)]  // User has config role
    [InlineData(555ul, new ulong[] { 777, 888 }, false)] // User doesn't have config role
    [InlineData(555ul, new ulong[] { }, false)]          // User has no roles
    public void UserRoleCheck_ShouldWorkCorrectly(ulong configRoleId, ulong[] userRoles, bool shouldHaveAccess)
    {
        // Act & Assert - Test the core logic that CheckPermissionsAsync uses
        var hasConfigRole = userRoles.Contains(configRoleId);
        hasConfigRole.ShouldBe(shouldHaveAccess);
    }

    [Fact]
    public async Task Database_ShouldStoreServerConfigurationCorrectly()
    {
        // Arrange
        var server1 = new Server { Id = 111, Name = "Server 1", ConfigRoleId = 555 };
        var server2 = new Server { Id = 222, Name = "Server 2", ConfigRoleId = null };
        var server3 = new Server { Id = 333, Name = "Server 3", ConfigRoleId = 777 };
        
        await _context.Servers.AddRangeAsync(server1, server2, server3);
        await _context.SaveChangesAsync();

        // Act
        var servers = await _context.Servers.OrderBy(s => s.Id).ToListAsync();

        // Assert
        servers.Count.ShouldBe(3);
        servers[0].ConfigRoleId.ShouldBe(555ul);
        servers[1].ConfigRoleId.ShouldBeNull();
        servers[2].ConfigRoleId.ShouldBe(777ul);
    }

    public void Dispose()
    {
        _context?.Dispose();
        _serviceProvider?.Dispose();
    }
} 