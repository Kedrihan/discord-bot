using System.Security.Claims;
using DiscordBot.Core.Permissions;
using DiscordBot.DTO.Permissions;
using DiscordBot.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DiscordBot.Tests.Core.Permissions;

[TestFixture]
public class TUPermissionServiceTests
{
    private TestBotDbContextFactory _dbContextFactory = null!;
    private FakeTimeProvider _timeProvider = null!;

    [SetUp]
    public void SetUp()
    {
        _dbContextFactory = new TestBotDbContextFactory();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero));
    }

    [TearDown]
    public void TearDown()
    {
        _dbContextFactory.Dispose();
    }

    private static ClaimsPrincipal CreateUser(string userId, bool isAuthenticated = true)
    {
        if (!isAuthenticated)
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, $"User_{userId}")
        ], "TestAuth");

        return new ClaimsPrincipal(identity);
    }

    [Test]
    public void IsSuperAdmin_ShouldReturnTrue_WhenOAuthNotConfigured()
    {
        var config = new ConfigurationBuilder().Build();
        var service = new PermissionService(_dbContextFactory, null, config, _timeProvider, NullLogger<PermissionService>.Instance);
        var user = CreateUser("999999999");

        var result = service.IsSuperAdmin(user);

        result.Should().BeTrue("when OAuth is not configured, any authenticated user is treated as SuperAdmin.");
    }

    [Test]
    public void IsSuperAdmin_ShouldReturnTrue_WhenUserMatchesConfiguredAdminId()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DISCORD_CLIENT_ID"] = "dummy_client_id",
                ["DISCORD_CLIENT_SECRET"] = "dummy_client_secret",
                ["DISCORD_ADMIN_USER_ID"] = "116893981526196227"
            })
            .Build();

        var service = new PermissionService(_dbContextFactory, null, config, _timeProvider, NullLogger<PermissionService>.Instance);
        var adminUser = CreateUser("116893981526196227");

        var result = service.IsSuperAdmin(adminUser);

        result.Should().BeTrue();
    }

    [Test]
    public void IsSuperAdmin_ShouldReturnFalse_WhenUserDoesNotMatchAdminId()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DISCORD_CLIENT_ID"] = "dummy_client_id",
                ["DISCORD_CLIENT_SECRET"] = "dummy_client_secret",
                ["DISCORD_ADMIN_USER_ID"] = "116893981526196227"
            })
            .Build();

        var service = new PermissionService(_dbContextFactory, null, config, _timeProvider, NullLogger<PermissionService>.Instance);
        var normalUser = CreateUser("999999999999");

        var result = service.IsSuperAdmin(normalUser);

        result.Should().BeFalse();
    }

    [Test]
    public void IsSuperAdmin_ShouldReturnFalse_WhenUserNotAuthenticated()
    {
        var config = new ConfigurationBuilder().Build();
        var service = new PermissionService(_dbContextFactory, null, config, _timeProvider, NullLogger<PermissionService>.Instance);
        var unauthenticatedUser = CreateUser("116893981526196227", isAuthenticated: false);

        var result = service.IsSuperAdmin(unauthenticatedUser);

        result.Should().BeFalse();
    }

    [Test]
    public async Task GuildAdmin_UpsertPermission_ShouldPersistAndGrantFeatureAccess()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DISCORD_CLIENT_ID"] = "dummy_client_id",
                ["DISCORD_CLIENT_SECRET"] = "dummy_client_secret",
                ["DISCORD_ADMIN_USER_ID"] = "116893981526196227"
            })
            .Build();

        var service = new PermissionService(_dbContextFactory, null, config, _timeProvider, NullLogger<PermissionService>.Instance);
        var guildAdmin = CreateUser("222333444");

        // Act: Accorder Twitch uniquement sur la Guild 1000
        await service.UpsertPermissionAsync("1000", "222333444", GuildFeatureFlagsDto.Twitch);

        // Assert
        service.HasGuildFeatureAccess(guildAdmin, "1000", GuildFeatureFlagsDto.Twitch).Should().BeTrue();
        service.HasGuildFeatureAccess(guildAdmin, "1000", GuildFeatureFlagsDto.YouTube).Should().BeFalse();
        service.HasGuildFeatureAccess(guildAdmin, "2000", GuildFeatureFlagsDto.Twitch).Should().BeFalse("no permission on Guild 2000");

        // Act: Mise à jour vers All (Twitch + YouTube)
        await service.UpsertPermissionAsync("1000", "222333444", GuildFeatureFlagsDto.All);

        service.HasGuildFeatureAccess(guildAdmin, "1000", GuildFeatureFlagsDto.Twitch).Should().BeTrue();
        service.HasGuildFeatureAccess(guildAdmin, "1000", GuildFeatureFlagsDto.YouTube).Should().BeTrue();
    }

    [Test]
    public async Task GuildAdmin_MultiGuild_ShouldIsolatePermissionsAcrossGuilds()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DISCORD_CLIENT_ID"] = "dummy_client_id",
                ["DISCORD_CLIENT_SECRET"] = "dummy_client_secret",
                ["DISCORD_ADMIN_USER_ID"] = "116893981526196227"
            })
            .Build();

        var service = new PermissionService(_dbContextFactory, null, config, _timeProvider, NullLogger<PermissionService>.Instance);
        var user = CreateUser("555666777");

        // User est admin Twitch sur Guild 100, et admin YouTube sur Guild 200
        await service.UpsertPermissionAsync("100", "555666777", GuildFeatureFlagsDto.Twitch);
        await service.UpsertPermissionAsync("200", "555666777", GuildFeatureFlagsDto.YouTube);

        // Assertions
        service.HasGuildFeatureAccess(user, "100", GuildFeatureFlagsDto.Twitch).Should().BeTrue();
        service.HasGuildFeatureAccess(user, "100", GuildFeatureFlagsDto.YouTube).Should().BeFalse();

        service.HasGuildFeatureAccess(user, "200", GuildFeatureFlagsDto.Twitch).Should().BeFalse();
        service.HasGuildFeatureAccess(user, "200", GuildFeatureFlagsDto.YouTube).Should().BeTrue();

        var twitchGuilds = service.GetAuthorizedGuildIds(user, GuildFeatureFlagsDto.Twitch);
        twitchGuilds.Should().ContainSingle().Which.Should().Be("100");

        var youTubeGuilds = service.GetAuthorizedGuildIds(user, GuildFeatureFlagsDto.YouTube);
        youTubeGuilds.Should().ContainSingle().Which.Should().Be("200");

        var allGuilds = service.GetAuthorizedGuildIds(user);
        allGuilds.Should().BeEquivalentTo(["100", "200"]);
    }

    [Test]
    public async Task GuildAdmin_RemovePermission_ShouldRevokeAccess()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DISCORD_CLIENT_ID"] = "dummy_client_id",
                ["DISCORD_CLIENT_SECRET"] = "dummy_client_secret",
                ["DISCORD_ADMIN_USER_ID"] = "116893981526196227"
            })
            .Build();

        var service = new PermissionService(_dbContextFactory, null, config, _timeProvider, NullLogger<PermissionService>.Instance);
        var user = CreateUser("888888");

        await service.UpsertPermissionAsync("300", "888888", GuildFeatureFlagsDto.All);
        service.HasGuildFeatureAccess(user, "300", GuildFeatureFlagsDto.Twitch).Should().BeTrue();

        await service.RemovePermissionAsync("300", "888888");
        service.HasGuildFeatureAccess(user, "300", GuildFeatureFlagsDto.Twitch).Should().BeFalse();
    }

    [Test]
    public async Task HasAnyAdminAccess_ShouldReturnExpectedResults()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DISCORD_CLIENT_ID"] = "dummy_client_id",
                ["DISCORD_CLIENT_SECRET"] = "dummy_client_secret",
                ["DISCORD_ADMIN_USER_ID"] = "116893981526196227"
            })
            .Build();

        var service = new PermissionService(_dbContextFactory, null, config, _timeProvider, NullLogger<PermissionService>.Instance);
        var superAdmin = CreateUser("116893981526196227");
        var guildAdmin = CreateUser("444444");
        var stranger = CreateUser("777777");

        await service.UpsertPermissionAsync("500", "444444", GuildFeatureFlagsDto.Twitch);

        service.HasAnyAdminAccess(superAdmin).Should().BeTrue("SuperAdmin always has admin access.");
        service.HasAnyAdminAccess(guildAdmin).Should().BeTrue("User has permission on Guild 500.");
        service.HasAnyAdminAccess(stranger).Should().BeFalse("Stranger has no permissions.");
    }

    [Test]
    public async Task GetAllPermissionsAsync_ShouldReturnPersistedList()
    {
        var config = new ConfigurationBuilder().Build();
        var service = new PermissionService(_dbContextFactory, null, config, _timeProvider, NullLogger<PermissionService>.Instance);

        await service.UpsertPermissionAsync("1", "user1", GuildFeatureFlagsDto.Twitch);
        await service.UpsertPermissionAsync("2", "user2", GuildFeatureFlagsDto.YouTube);

        var list = await service.GetAllPermissionsAsync();

        list.Should().HaveCount(2);
        list.Should().Contain(p => p.GuildId == "1" && p.UserId == "user1" && p.FeatureFlags == GuildFeatureFlagsDto.Twitch);
        list.Should().Contain(p => p.GuildId == "2" && p.UserId == "user2" && p.FeatureFlags == GuildFeatureFlagsDto.YouTube);
    }

    [Test]
    public void IsChannelInGuild_ShouldReturnFalse_WhenChannelOrGuildIsNullOrWhitespace()
    {
        var config = new ConfigurationBuilder().Build();
        var service = new PermissionService(_dbContextFactory, null, config, _timeProvider, NullLogger<PermissionService>.Instance);

        service.IsChannelInGuild("", "123").Should().BeFalse();
        service.IsChannelInGuild("123", "").Should().BeFalse();
        service.IsChannelInGuild("abc", "123").Should().BeFalse();
    }

    [Test]
    public void IsChannelInGuild_ShouldReturnTrue_WhenDiscordClientIsNull_AndChannelIsValidSnowflake()
    {
        var config = new ConfigurationBuilder().Build();
        var service = new PermissionService(_dbContextFactory, null, config, _timeProvider, NullLogger<PermissionService>.Instance);

        service.IsChannelInGuild("1234567890", "9876543210").Should().BeTrue();
    }

    [Test]
    public async Task GetCachedPermissions_ConcurrentReads_ShouldBeDeterministicAndSafe()
    {
        var config = new ConfigurationBuilder().Build();
        var service = new PermissionService(_dbContextFactory, null, config, _timeProvider, NullLogger<PermissionService>.Instance);
        var user = CreateUser("user_concurrent");

        await service.UpsertPermissionAsync("100", "user_concurrent", GuildFeatureFlagsDto.All);

        var tasks = Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
        {
            return service.HasGuildFeatureAccess(user, "100", GuildFeatureFlagsDto.Twitch);
        }));

        var results = await Task.WhenAll(tasks);
        results.Should().OnlyContain(r => r == true);
    }
}
