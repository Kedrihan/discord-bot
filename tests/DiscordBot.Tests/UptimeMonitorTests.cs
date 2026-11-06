using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Reflection;
using Discord;
using Discord.WebSocket;
using DiscordBot.Components.Pages;
using DiscordBot.Models;
using DiscordBot.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DiscordBot.Tests;

[TestFixture]
public class UptimeMonitorTests
{
    private MockFileSystem _fileSystem = null!;
    private FakeTimeProvider _timeProvider = null!;

    [SetUp]
    public void SetUp()
    {
        _fileSystem = new MockFileSystem();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 11, 6, 10, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public void FormatDuration_ShouldFormatCorrectly_AcrossTimeScales()
    {
        UptimeMonitorService.FormatDuration(TimeSpan.FromSeconds(45)).Should().Be("45s");
        UptimeMonitorService.FormatDuration(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(12)).Should().Be("5m 12s");
        UptimeMonitorService.FormatDuration(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(30)).Should().Be("2h 15m 30s");
        UptimeMonitorService.FormatDuration(TimeSpan.FromSeconds(0)).Should().Be("1s");
    }

    [TestCase("mafreebox.freebox.fr", null, "http://mafreebox.freebox.fr")]
    [TestCase("mafreebox.freebox.fr", 443, "https://mafreebox.freebox.fr")]
    [TestCase("mafreebox.freebox.fr", 8080, "http://mafreebox.freebox.fr:8080")]
    [TestCase("https://example.com/api", null, "https://example.com/api")]
    [TestCase("http://192.168.1.1:80", 80, "http://192.168.1.1:80")]
    public void FormatHttpUrl_ShouldResolveProtocolAndPort(string hostOrUrl, int? port, string expectedUrl)
    {
        var target = new MonitoredTarget
        {
            HostOrUrl = hostOrUrl,
            Port = port
        };

        var resolved = UptimeMonitorService.FormatHttpUrl(target);
        resolved.Should().Be(expectedUrl);
    }

    [Test]
    public void BuildDownEmbed_ShouldProduceRedEmbedWithIncidentDetails()
    {
        var monitorService = CreateMonitorService(new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var target = new MonitoredTarget
        {
            Id = "freebox1",
            Name = "Freebox Delta",
            Protocol = TargetProtocol.Http,
            HostOrUrl = "http://192.168.1.254/api_version",
            FailureThreshold = 2
        };

        var embed = monitorService.BuildDownEmbed(target, 2, "Code HTTP 500 (Internal Server Error)");

        embed.Color.Should().Be(Color.Red);
        embed.Title.Should().Contain("Incident détecté");
        embed.Title.Should().Contain("Freebox Delta");
        embed.Fields.Should().Contain(f => f.Name == "Protocole" && f.Value == "HTTP(S)");
        embed.Fields.Should().Contain(f => f.Name == "Cible" && f.Value == "http://192.168.1.254/api_version");
        embed.Fields.Should().Contain(f => f.Name == "Échecs consécutifs" && f.Value == "2");
        embed.Fields.Should().Contain(f => f.Name == "Détail de l'erreur" && f.Value.Contains("500"));
        embed.Footer?.Text.Should().Be("DiscordBot Uptime Monitor");
    }

    [Test]
    public void BuildUpEmbed_ShouldProduceGreenEmbedWithRecoveryDetails()
    {
        var monitorService = CreateMonitorService(new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var target = new MonitoredTarget
        {
            Id = "freebox1",
            Name = "Freebox Delta",
            Protocol = TargetProtocol.Http,
            HostOrUrl = "http://192.168.1.254/api_version"
        };

        var embed = monitorService.BuildUpEmbed(target, TimeSpan.FromMinutes(12) + TimeSpan.FromSeconds(34), 18);

        embed.Color.Should().Be(Color.Green);
        embed.Title.Should().Contain("Rétablissement");
        embed.Title.Should().Contain("Freebox Delta");
        embed.Fields.Should().Contain(f => f.Name == "Durée de l'incident" && f.Value == "12m 34s");
        embed.Fields.Should().Contain(f => f.Name == "Latence" && f.Value == "18 ms");
        embed.Footer?.Text.Should().Be("DiscordBot Uptime Monitor");
    }

    [Test]
    public async Task CheckHttpTargetAsync_ShouldReturnSuccess_WhenStatusCode200()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var monitorService = CreateMonitorService(handler);

        var target = new MonitoredTarget
        {
            HostOrUrl = "https://google.com",
            Protocol = TargetProtocol.Http,
            TimeoutSeconds = 5
        };

        var result = await monitorService.CheckHttpTargetAsync(target, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
    }

    [Test]
    public async Task CheckHttpTargetAsync_ShouldReturnFailure_WhenStatusCode503()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            ReasonPhrase = "Service Unavailable"
        });
        var monitorService = CreateMonitorService(handler);

        var target = new MonitoredTarget
        {
            HostOrUrl = "https://example.com/api",
            Protocol = TargetProtocol.Http,
            TimeoutSeconds = 5
        };

        var result = await monitorService.CheckHttpTargetAsync(target, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("503");
        result.ErrorMessage.Should().Contain("Service Unavailable");
    }

    [Test]
    public async Task CheckTcpTargetAsync_ShouldReturnFailure_WhenPortIsMissingOrInvalid()
    {
        var monitorService = CreateMonitorService(new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        var target = new MonitoredTarget
        {
            HostOrUrl = "127.0.0.1",
            Protocol = TargetProtocol.Tcp,
            Port = null
        };

        var result = await monitorService.CheckTcpTargetAsync(target, CancellationToken.None);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Port TCP invalide");

        target.Port = 70000;
        var result2 = await monitorService.CheckTcpTargetAsync(target, CancellationToken.None);
        result2.Success.Should().BeFalse();
        result2.ErrorMessage.Should().Contain("Port TCP invalide");
    }

    [Test]
    public async Task AntiFlapping_StateMachine_ShouldTransitionToDown_OnlyAfterThresholdReached()
    {
        var configService = new UptimeConfigService(_fileSystem, NullLogger<UptimeConfigService>.Instance);
        var stateService = new UptimeStateService(_fileSystem, NullLogger<UptimeStateService>.Instance);

        var target = new MonitoredTarget
        {
            Id = "target-1",
            Name = "Server Web",
            Protocol = TargetProtocol.Http,
            HostOrUrl = "https://failing-server.internal",
            FailureThreshold = 2,
            IsEnabled = true
        };

        await configService.AddTargetAsync(target);

        // Serveur retourne 500
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var monitorService = CreateMonitorService(handler, configService, stateService);

        // Étape 1 : 1er échec -> pas encore Down (FailureThreshold = 2)
        var result1 = await monitorService.CheckAllTargetsAsync();
        result1.Success.Should().BeTrue();

        var stateAfterFirstFailure = stateService.GetTargetState("target-1");
        stateAfterFirstFailure.Should().NotBeNull();
        stateAfterFirstFailure!.ConsecutiveFailures.Should().Be(1);
        stateAfterFirstFailure.Status.Should().Be(TargetStatus.Pending); // pas encore Down !
        stateAfterFirstFailure.DownSince.Should().BeNull();

        // On avance le temps d'une minute
        _timeProvider.Advance(TimeSpan.FromMinutes(1));

        // Étape 2 : 2ème échec -> Seuil atteint -> Bascule vers Down
        var result2 = await monitorService.CheckAllTargetsAsync();
        result2.Success.Should().BeTrue();

        var stateAfterSecondFailure = stateService.GetTargetState("target-1");
        stateAfterSecondFailure!.ConsecutiveFailures.Should().Be(2);
        stateAfterSecondFailure.Status.Should().Be(TargetStatus.Down);
        stateAfterSecondFailure.DownSince.Should().Be(_timeProvider.GetUtcNow());

        // Étape 3 : 3ème échec -> Reste Down, DownSince ne doit pas être écrasé
        var initialDownSince = stateAfterSecondFailure.DownSince;
        _timeProvider.Advance(TimeSpan.FromMinutes(2));

        await monitorService.CheckAllTargetsAsync();
        var stateAfterThirdFailure = stateService.GetTargetState("target-1");
        stateAfterThirdFailure!.ConsecutiveFailures.Should().Be(3);
        stateAfterThirdFailure.Status.Should().Be(TargetStatus.Down);
        stateAfterThirdFailure.DownSince.Should().Be(initialDownSince);
    }

    [Test]
    public async Task AntiFlapping_StateMachine_ShouldTransitionToUp_AndCalculateDowntime_OnRecovery()
    {
        var configService = new UptimeConfigService(_fileSystem, NullLogger<UptimeConfigService>.Instance);
        var stateService = new UptimeStateService(_fileSystem, NullLogger<UptimeStateService>.Instance);

        var target = new MonitoredTarget
        {
            Id = "target-recovery",
            Name = "Database Server",
            Protocol = TargetProtocol.Http,
            HostOrUrl = "https://db.internal",
            FailureThreshold = 1,
            IsEnabled = true
        };
        await configService.AddTargetAsync(target);

        var shouldFail = true;
        var handler = new MockHttpMessageHandler(_ =>
        {
            if (shouldFail) return new HttpResponseMessage(HttpStatusCode.BadGateway);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var monitorService = CreateMonitorService(handler, configService, stateService);

        // Panne immédiate
        await monitorService.CheckAllTargetsAsync();
        var downState = stateService.GetTargetState("target-recovery");
        downState!.Status.Should().Be(TargetStatus.Down);
        downState.DownSince.Should().Be(_timeProvider.GetUtcNow());

        // Avancer le temps de 15 minutes et 30 secondes
        _timeProvider.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(30));

        // Rétablissement du service
        shouldFail = false;
        await monitorService.CheckAllTargetsAsync();

        var recoveredState = stateService.GetTargetState("target-recovery");
        recoveredState!.Status.Should().Be(TargetStatus.Up);
        recoveredState.ConsecutiveFailures.Should().Be(0);
        recoveredState.DownSince.Should().BeNull();
        recoveredState.LastErrorMessage.Should().BeNull();
    }

    [Test]
    public async Task UptimeConfigService_CRUD_ShouldPersistAcrossInstances()
    {
        var configService1 = new UptimeConfigService(_fileSystem, NullLogger<UptimeConfigService>.Instance);

        var target1 = new MonitoredTarget
        {
            Id = "box1",
            Name = "Freebox",
            Protocol = TargetProtocol.Http,
            HostOrUrl = "http://192.168.1.254",
            IsEnabled = true
        };

        var target2 = new MonitoredTarget
        {
            Id = "vps1",
            Name = "VPS SSH",
            Protocol = TargetProtocol.Tcp,
            HostOrUrl = "vps.mydomain.com",
            Port = 22,
            IsEnabled = true
        };

        await configService1.AddTargetAsync(target1);
        await configService1.AddTargetAsync(target2);

        // Instance 2 recharge depuis le disque virtuel MockFileSystem
        var configService2 = new UptimeConfigService(_fileSystem, NullLogger<UptimeConfigService>.Instance);
        var loaded = configService2.GetSettings();
        loaded.Targets.Should().HaveCount(2);
        loaded.Targets.Should().Contain(t => t.Id == "box1" && t.Name == "Freebox");
        loaded.Targets.Should().Contain(t => t.Id == "vps1" && t.Port == 22);

        // Modification & Toggle
        await configService2.ToggleTargetAsync("box1");
        var toggled = configService2.GetSettings().Targets.First(t => t.Id == "box1");
        toggled.IsEnabled.Should().BeFalse();

        // Suppression
        await configService2.DeleteTargetAsync("vps1");
        configService2.GetSettings().Targets.Should().HaveCount(1);
    }

    [Test]
    public async Task UptimeStateService_ShouldSaveAndRetrieveTargetStates()
    {
        var stateService1 = new UptimeStateService(_fileSystem, NullLogger<UptimeStateService>.Instance);

        var now = _timeProvider.GetUtcNow();
        var targetState = new UptimeTargetState
        {
            TargetId = "target-abc",
            Status = TargetStatus.Up,
            ConsecutiveFailures = 0,
            LastResponseTimeMs = 42,
            LastCheckedAt = now,
            LastStatusChangeAt = now
        };

        await stateService1.UpdateTargetStateAsync(targetState);

        var stateService2 = new UptimeStateService(_fileSystem, NullLogger<UptimeStateService>.Instance);
        var retrieved = stateService2.GetTargetState("target-abc");

        retrieved.Should().NotBeNull();
        retrieved!.Status.Should().Be(TargetStatus.Up);
        retrieved.LastResponseTimeMs.Should().Be(42);

        await stateService2.RemoveTargetStateAsync("target-abc");
        stateService2.GetTargetState("target-abc").Should().BeNull();
    }

    [Test]
    public void SystemHealthService_GetSystemMetrics_ShouldReturnValidMetrics()
    {
        var discordClient = new DiscordSocketClient(new DiscordSocketConfig());
        var services = new ServiceCollection().BuildServiceProvider();
        var botSettings = new BotSettingsService(_timeProvider);
        var healthService = new SystemHealthService(discordClient, services, botSettings, _timeProvider, NullLogger<SystemHealthService>.Instance);

        var metrics = healthService.GetSystemMetrics();

        metrics.Should().NotBeNull();
        metrics.WorkingSetBytes.Should().BeGreaterThan(0);
        metrics.ThreadCount.Should().BeGreaterThan(0);
        metrics.DotNetVersion.Should().NotBeNullOrWhiteSpace();
        metrics.OsDescription.Should().NotBeNullOrWhiteSpace();
        metrics.Architecture.Should().NotBeNullOrWhiteSpace();
        metrics.Uptime.Should().BeGreaterOrEqualTo(TimeSpan.Zero);
    }

    [Test]
    public void SystemHealthService_GetServicesHealth_ShouldIncludeAllExpectedServices()
    {
        var discordClient = new DiscordSocketClient(new DiscordSocketConfig());
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddSingleton(new TechWatchConfigService(_fileSystem, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), NullLogger<TechWatchConfigService>.Instance));
        serviceCollection.AddSingleton(new MorningBriefingConfigService(_fileSystem, NullLogger<MorningBriefingConfigService>.Instance));
        serviceCollection.AddSingleton(new UptimeConfigService(_fileSystem, NullLogger<UptimeConfigService>.Instance));

        var sp = serviceCollection.BuildServiceProvider();
        var botSettings = new BotSettingsService(_timeProvider);
        var healthService = new SystemHealthService(discordClient, sp, botSettings, _timeProvider, NullLogger<SystemHealthService>.Instance);

        var healthList = healthService.GetServicesHealth();

        healthList.Should().Contain(s => s.Name == "Passerelle Discord");
        healthList.Should().Contain(s => s.Name == "Veille Technologique");
        healthList.Should().Contain(s => s.Name == "Briefing Matin");
        healthList.Should().Contain(s => s.Name == "Uptime Monitor");
    }

    [Test]
    public void SanitizeUntrustedText_ShouldNeutralizeMentions()
    {
        var input = "Target @everyone and @here with <@123456789> and <@&987654321>";
        var sanitized = UptimeMonitorService.SanitizeUntrustedText(input);

        sanitized.Should().NotContain("@everyone");
        sanitized.Should().NotContain("@here");
        sanitized.Should().NotContain("<@");
        sanitized.Should().Contain("@\u200beveryone");
        sanitized.Should().Contain("@\u200bhere");
        sanitized.Should().Contain("<\u200b@123456789>");
        sanitized.Should().Contain("<\u200b@&987654321>");
    }

    [Test]
    public void BuildDownEmbed_ShouldSanitizeMentionsInTargetNameHostAndError()
    {
        var monitorService = CreateMonitorService(new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var maliciousTarget = new MonitoredTarget
        {
            Id = "exploit1",
            Name = "Attacker @everyone",
            HostOrUrl = "http://badhost.local/<@123456789>",
            Protocol = TargetProtocol.Http
        };

        var embed = monitorService.BuildDownEmbed(maliciousTarget, 3, "Failed with error @here pinging <@&999>");

        embed.Title.Should().NotContain("@everyone");
        embed.Title.Should().Contain("@\u200beveryone");

        var hostField = embed.Fields.First(f => f.Name == "Cible");
        hostField.Value.Should().NotContain("<@");
        hostField.Value.Should().Contain("<\u200b@");

        var errorField = embed.Fields.First(f => f.Name == "Détail de l'erreur");
        errorField.Value.Should().NotContain("@here");
        errorField.Value.Should().NotContain("<@&");
        errorField.Value.Should().Contain("@\u200bhere");
        errorField.Value.Should().Contain("<\u200b@&999>");
    }

    [Test]
    public void BuildUpEmbed_ShouldSanitizeMentionsInTargetNameAndHost()
    {
        var monitorService = CreateMonitorService(new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var maliciousTarget = new MonitoredTarget
        {
            Id = "exploit2",
            Name = "Server @here",
            HostOrUrl = "https://safe.local/<@&111222333>",
            Protocol = TargetProtocol.Http
        };

        var embed = monitorService.BuildUpEmbed(maliciousTarget, TimeSpan.FromMinutes(5), 25);

        embed.Title.Should().NotContain("@here");
        embed.Title.Should().Contain("@\u200bhere");

        var hostField = embed.Fields.First(f => f.Name == "Cible");
        hostField.Value.Should().NotContain("<@&");
        hostField.Value.Should().Contain("<\u200b@&");
    }

    private UptimeMonitorService CreateMonitorService(
        HttpMessageHandler httpHandler,
        UptimeConfigService? configService = null,
        UptimeStateService? stateService = null)
    {
        var factory = new MockHttpClientFactory(httpHandler);
        var discordClient = new DiscordSocketClient(new DiscordSocketConfig());
        var config = configService ?? new UptimeConfigService(_fileSystem, NullLogger<UptimeConfigService>.Instance);
        var state = stateService ?? new UptimeStateService(_fileSystem, NullLogger<UptimeStateService>.Instance);

        return new UptimeMonitorService(
            factory,
            discordClient,
            config,
            state,
            _timeProvider,
            NullLogger<UptimeMonitorService>.Instance
        );
    }

    private class MockHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private class MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }
}
