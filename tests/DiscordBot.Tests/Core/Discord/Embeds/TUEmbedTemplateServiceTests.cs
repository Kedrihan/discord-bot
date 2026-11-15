using Discord.WebSocket;
using DiscordBot.Core.Discord.Embeds;
using DiscordBot.DTO.Embeds;
using DiscordBot.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DiscordBot.Tests.Core.Discord.Embeds;

[TestFixture]
public class TUEmbedTemplateServiceTests
{
    private TestBotDbContextFactory _dbContextFactory = null!;
    private FakeTimeProvider _timeProvider = null!;
    private EmbedTemplateRenderer _renderer = null!;
    private EmbedTemplateService _service = null!;
    private DiscordSocketClient _discordClient = null!;

    [SetUp]
    public void SetUp()
    {
        _dbContextFactory = new TestBotDbContextFactory();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero));
        _renderer = new EmbedTemplateRenderer(_timeProvider);
        _discordClient = new DiscordSocketClient();
        _service = new EmbedTemplateService(
            _dbContextFactory,
            _discordClient,
            _renderer,
            _timeProvider,
            NullLogger<EmbedTemplateService>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _dbContextFactory.Dispose();
        _discordClient.Dispose();
    }

    [Test]
    public async Task GetTemplateAsync_ShouldReturnDefault_WhenDatabaseIsEmpty()
    {
        // Act
        var template = await _service.GetTemplateAsync(EmbedTemplateTypeDto.TwitchLive);

        // Assert
        template.Should().NotBeNull();
        template.TemplateType.Should().Be(EmbedTemplateTypeDto.TwitchLive);
        template.ColorHex.Should().Be("#9146FF");
        template.TitleTemplate.Should().Be("{StreamTitle}");
        template.CustomFields.Should().HaveCount(3);
    }

    [Test]
    public async Task SaveTemplateAsync_ShouldPersistGlobalTemplate_AndRetrieveIt()
    {
        // Arrange
        var customGlobal = new DiscordEmbedTemplateDto
        {
            TemplateType = EmbedTemplateTypeDto.YouTubeVideo,
            ColorHex = "#123456",
            TitleTemplate = "Titre personnalisé: {VideoTitle}",
            DescriptionTemplate = "Description globale",
            EnableAuthor = false,
            CustomFields = [new("TestChamp", "{VideoType}", Inline: false, IsEnabled: true, Order: 0)]
        };

        // Act
        await _service.SaveTemplateAsync(customGlobal);
        var retrieved = await _service.GetTemplateAsync(EmbedTemplateTypeDto.YouTubeVideo);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved.ColorHex.Should().Be("#123456");
        retrieved.TitleTemplate.Should().Be("Titre personnalisé: {VideoTitle}");
        retrieved.DescriptionTemplate.Should().Be("Description globale");
        retrieved.EnableAuthor.Should().BeFalse();
        retrieved.CustomFields.Should().HaveCount(1);
        retrieved.CustomFields[0].Name.Should().Be("TestChamp");
    }

    [Test]
    public async Task GetTemplateAsync_ShouldRespectHierarchicalFallback()
    {
        // 1. Initially empty: returns default
        var initial = await _service.GetTemplateAsync(EmbedTemplateTypeDto.TwitchLive, guildId: "guild1", channelTargetId: "streamer1");
        initial.TitleTemplate.Should().Be("{StreamTitle}");

        // 2. Save global override
        await _service.SaveTemplateAsync(new DiscordEmbedTemplateDto
        {
            TemplateType = EmbedTemplateTypeDto.TwitchLive,
            GuildId = "",
            ChannelTargetId = "",
            TitleTemplate = "Global: {StreamTitle}"
        });

        var afterGlobal = await _service.GetTemplateAsync(EmbedTemplateTypeDto.TwitchLive, guildId: "guild1", channelTargetId: "streamer1");
        afterGlobal.TitleTemplate.Should().Be("Global: {StreamTitle}");

        // 3. Save guild-wide override
        await _service.SaveTemplateAsync(new DiscordEmbedTemplateDto
        {
            TemplateType = EmbedTemplateTypeDto.TwitchLive,
            GuildId = "guild1",
            ChannelTargetId = "",
            TitleTemplate = "Guild1: {StreamTitle}"
        });

        var afterGuild = await _service.GetTemplateAsync(EmbedTemplateTypeDto.TwitchLive, guildId: "guild1", channelTargetId: "streamer1");
        afterGuild.TitleTemplate.Should().Be("Guild1: {StreamTitle}");

        // Another guild still gets global
        var otherGuild = await _service.GetTemplateAsync(EmbedTemplateTypeDto.TwitchLive, guildId: "guild2", channelTargetId: "streamer1");
        otherGuild.TitleTemplate.Should().Be("Global: {StreamTitle}");

        // 4. Save channel-specific override
        await _service.SaveTemplateAsync(new DiscordEmbedTemplateDto
        {
            TemplateType = EmbedTemplateTypeDto.TwitchLive,
            GuildId = "guild1",
            ChannelTargetId = "streamer1",
            TitleTemplate = "Streamer1: {StreamTitle}"
        });

        var afterSpecific = await _service.GetTemplateAsync(EmbedTemplateTypeDto.TwitchLive, guildId: "guild1", channelTargetId: "streamer1");
        afterSpecific.TitleTemplate.Should().Be("Streamer1: {StreamTitle}");

        // Another streamer on guild1 gets guild1 template
        var otherStreamer = await _service.GetTemplateAsync(EmbedTemplateTypeDto.TwitchLive, guildId: "guild1", channelTargetId: "streamer2");
        otherStreamer.TitleTemplate.Should().Be("Guild1: {StreamTitle}");
    }

    [Test]
    public async Task ResetTemplateAsync_ShouldRemoveOverride_AndFallback()
    {
        // Arrange
        await _service.SaveTemplateAsync(new DiscordEmbedTemplateDto
        {
            TemplateType = EmbedTemplateTypeDto.TwitchClip,
            GuildId = "guild1",
            ChannelTargetId = "streamer1",
            TitleTemplate = "Override Clip Title"
        });

        // Verify saved
        var beforeReset = await _service.GetTemplateAsync(EmbedTemplateTypeDto.TwitchClip, guildId: "guild1", channelTargetId: "streamer1");
        beforeReset.TitleTemplate.Should().Be("Override Clip Title");

        // Act
        var resetResult = await _service.ResetTemplateAsync(EmbedTemplateTypeDto.TwitchClip, guildId: "guild1", channelTargetId: "streamer1");

        // Assert
        resetResult.Should().BeTrue();
        var afterReset = await _service.GetTemplateAsync(EmbedTemplateTypeDto.TwitchClip, guildId: "guild1", channelTargetId: "streamer1");
        afterReset.TitleTemplate.Should().Be("{ClipTitle}"); // falls back to default
    }

    [Test]
    public void GetAvailableVariables_ShouldReturnNonEmptyList_ForEachType()
    {
        foreach (EmbedTemplateTypeDto type in Enum.GetValues<EmbedTemplateTypeDto>())
        {
            var vars = _service.GetAvailableVariables(type);
            vars.Should().NotBeEmpty();
            vars.Should().AllSatisfy(v =>
            {
                v.VariableName.Should().StartWith("{").And.EndWith("}");
                v.Description.Should().NotBeNullOrWhiteSpace();
            });
        }
    }

    [Test]
    public void GetSampleData_ShouldReturnRealisticValues_ForEachType()
    {
        foreach (EmbedTemplateTypeDto type in Enum.GetValues<EmbedTemplateTypeDto>())
        {
            var sample = _service.GetSampleData(type);
            sample.Variables.Should().NotBeEmpty();
            sample.Timestamp.Should().Be(_timeProvider.GetUtcNow());
        }
    }

    [Test]
    public async Task SendTestEmbedAsync_ShouldReturnError_WhenDiscordChannelIdIsInvalid()
    {
        // Arrange
        var template = _service.GetDefaultTemplate(EmbedTemplateTypeDto.TwitchLive);

        // Act
        var result = await _service.SendTestEmbedAsync(EmbedTemplateTypeDto.TwitchLive, template, "not-a-number");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("valide");
    }

    [Test]
    public async Task SendTestEmbedAsync_ShouldReturnError_WhenChannelNotFound()
    {
        // Arrange
        var template = _service.GetDefaultTemplate(EmbedTemplateTypeDto.TwitchLive);

        // Act
        var result = await _service.SendTestEmbedAsync(EmbedTemplateTypeDto.TwitchLive, template, "123456789012345678");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("introuvable");
    }
}
