using System.Diagnostics;
using System.Net.Sockets;
using Discord;
using Discord.WebSocket;
using DiscordBot.Models;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public class UptimeMonitorService(
    IHttpClientFactory httpClientFactory,
    DiscordSocketClient discordClient,
    UptimeConfigService configService,
    UptimeStateService stateService,
    TimeProvider timeProvider,
    ILogger<UptimeMonitorService> logger)
{
    private readonly SemaphoreSlim _checkLock = new(1, 1);

    public event Action? OnTargetsChecked;

    public async Task<UptimeExecutionResult> CheckAllTargetsAsync(CancellationToken ct = default)
    {
        var settings = configService.GetSettings();
        if (!settings.IsEnabled)
        {
            return new UptimeExecutionResult(true, "Uptime monitoring désactivé.");
        }

        var enabledTargets = settings.Targets.Where(t => t.IsEnabled).ToList();
        if (enabledTargets.Count == 0)
        {
            return new UptimeExecutionResult(true, "Aucune cible active à surveiller.");
        }

        if (!await _checkLock.WaitAsync(0, ct))
        {
            return new UptimeExecutionResult(false, "Une vérification des cibles est déjà en cours.");
        }

        try
        {
            var checkedCount = 0;
            var now = timeProvider.GetUtcNow();

            foreach (var target in enabledTargets)
            {
                if (ct.IsCancellationRequested) break;

                var result = await PerformCheckAsync(target, ct);
                checkedCount++;

                var targetState = stateService.GetTargetState(target.Id) ?? new UptimeTargetState
                {
                    TargetId = target.Id,
                    Status = TargetStatus.Pending
                };

                if (result.Success)
                {
                    await HandleSuccessStateAsync(target, targetState, result, now, ct);
                }
                else
                {
                    await HandleFailureStateAsync(target, targetState, result, now, ct);
                }
            }

            OnTargetsChecked?.Invoke();
            return new UptimeExecutionResult(true, $"{checkedCount} cibles surveillées avec succès.", checkedCount);
        }
        finally
        {
            _checkLock.Release();
        }
    }

    public async Task<TargetCheckResult> TestTargetAsync(MonitoredTarget target, CancellationToken ct = default)
    {
        return await PerformCheckAsync(target, ct);
    }

    private async Task<TargetCheckResult> PerformCheckAsync(MonitoredTarget target, CancellationToken ct)
    {
        return target.Protocol switch
        {
            TargetProtocol.Http => await CheckHttpTargetAsync(target, ct),
            TargetProtocol.Tcp => await CheckTcpTargetAsync(target, ct),
            _ => new TargetCheckResult(false, 0, "Protocole non supporté")
        };
    }

    public async Task<TargetCheckResult> CheckHttpTargetAsync(MonitoredTarget target, CancellationToken ct)
    {
        var url = FormatHttpUrl(target);
        var timeoutSeconds = Math.Clamp(target.TimeoutSeconds > 0 ? target.TimeoutSeconds : 5, 1, 60);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var sw = Stopwatch.StartNew();
        try
        {
            var client = httpClientFactory.CreateClient("UptimeMonitor");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                return new TargetCheckResult(true, sw.ElapsedMilliseconds);
            }

            var statusCode = (int)response.StatusCode;
            return new TargetCheckResult(false, sw.ElapsedMilliseconds, $"Code HTTP {statusCode} ({response.ReasonPhrase})");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            return new TargetCheckResult(false, sw.ElapsedMilliseconds, $"Délai d'attente dépassé ({timeoutSeconds}s)");
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            return new TargetCheckResult(false, sw.ElapsedMilliseconds, $"Erreur de requête : {ex.Message}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TargetCheckResult(false, sw.ElapsedMilliseconds, $"Erreur : {ex.Message}");
        }
    }

    public async Task<TargetCheckResult> CheckTcpTargetAsync(MonitoredTarget target, CancellationToken ct)
    {
        if (!target.Port.HasValue || target.Port.Value <= 0 || target.Port.Value > 65535)
        {
            return new TargetCheckResult(false, 0, "Port TCP invalide ou manquant");
        }

        var timeoutSeconds = Math.Clamp(target.TimeoutSeconds > 0 ? target.TimeoutSeconds : 5, 1, 60);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var sw = Stopwatch.StartNew();
        using var tcpClient = new TcpClient();
        try
        {
            await tcpClient.ConnectAsync(target.HostOrUrl, target.Port.Value, cts.Token);
            sw.Stop();
            return new TargetCheckResult(true, sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            return new TargetCheckResult(false, sw.ElapsedMilliseconds, $"Délai de connexion TCP dépassé ({timeoutSeconds}s)");
        }
        catch (SocketException ex)
        {
            sw.Stop();
            return new TargetCheckResult(false, sw.ElapsedMilliseconds, $"Erreur socket ({ex.SocketErrorCode}) : {ex.Message}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TargetCheckResult(false, sw.ElapsedMilliseconds, $"Erreur TCP : {ex.Message}");
        }
    }

    private async Task HandleSuccessStateAsync(
        MonitoredTarget target,
        UptimeTargetState targetState,
        TargetCheckResult result,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var wasDown = targetState.Status == TargetStatus.Down;

        if (wasDown)
        {
            var downSince = targetState.DownSince ?? targetState.LastStatusChangeAt ?? now;
            var downtime = now - downSince;
            if (downtime < TimeSpan.Zero) downtime = TimeSpan.Zero;

            targetState.Status = TargetStatus.Up;
            targetState.LastStatusChangeAt = now;
            targetState.DownSince = null;

            logger.LogInformation("Rétablissement détecté pour {TargetName} ({Host}). Indisponibilité: {Downtime}",
                target.Name, target.HostOrUrl, downtime);

            var upEmbed = BuildUpEmbed(target, downtime, result.ResponseTimeMs);
            await SendAlertEmbedAsync(target, upEmbed, ct);
        }
        else if (targetState.Status != TargetStatus.Up)
        {
            targetState.Status = TargetStatus.Up;
            targetState.LastStatusChangeAt = now;
        }

        targetState.ConsecutiveFailures = 0;
        targetState.LastResponseTimeMs = result.ResponseTimeMs;
        targetState.LastCheckedAt = now;
        targetState.LastErrorMessage = null;

        await stateService.UpdateTargetStateAsync(targetState, ct);
    }

    private async Task HandleFailureStateAsync(
        MonitoredTarget target,
        UptimeTargetState targetState,
        TargetCheckResult result,
        DateTimeOffset now,
        CancellationToken ct)
    {
        targetState.ConsecutiveFailures++;
        targetState.LastResponseTimeMs = result.ResponseTimeMs;
        targetState.LastCheckedAt = now;
        targetState.LastErrorMessage = result.ErrorMessage;

        var threshold = Math.Max(1, target.FailureThreshold);
        if (targetState.ConsecutiveFailures >= threshold && targetState.Status != TargetStatus.Down)
        {
            targetState.Status = TargetStatus.Down;
            targetState.DownSince = now;
            targetState.LastStatusChangeAt = now;

            logger.LogWarning("Incident détecté pour {TargetName} ({Host}) après {Failures} échecs : {Error}",
                target.Name, target.HostOrUrl, targetState.ConsecutiveFailures, result.ErrorMessage);

            var downEmbed = BuildDownEmbed(target, targetState.ConsecutiveFailures, result.ErrorMessage);
            await SendAlertEmbedAsync(target, downEmbed, ct);
        }

        await stateService.UpdateTargetStateAsync(targetState, ct);
    }

    public Embed BuildDownEmbed(MonitoredTarget target, int consecutiveFailures, string? errorMessage)
    {
        var protocolStr = target.Protocol == TargetProtocol.Http ? "HTTP(S)" : $"TCP ({target.Port})";
        var safeName = SanitizeUntrustedText(target.Name);
        var safeHost = SanitizeUntrustedText(target.HostOrUrl);
        var safeError = SanitizeUntrustedText(errorMessage ?? "Aucune réponse");

        return new EmbedBuilder()
            .WithTitle($"🚨 Incident détecté : {safeName}")
            .WithDescription($"La cible ne répond plus et a dépassé le seuil de {consecutiveFailures} échec(s) consécutif(s).")
            .WithColor(Color.Red)
            .AddField("Protocole", protocolStr, inline: true)
            .AddField("Cible", safeHost, inline: true)
            .AddField("Échecs consécutifs", consecutiveFailures.ToString(), inline: true)
            .AddField("Détail de l'erreur", safeError, inline: false)
            .WithFooter("DiscordBot Uptime Monitor")
            .WithTimestamp(timeProvider.GetUtcNow())
            .Build();
    }

    public Embed BuildUpEmbed(MonitoredTarget target, TimeSpan downtime, long responseTimeMs)
    {
        var protocolStr = target.Protocol == TargetProtocol.Http ? "HTTP(S)" : $"TCP ({target.Port})";
        var safeName = SanitizeUntrustedText(target.Name);
        var safeHost = SanitizeUntrustedText(target.HostOrUrl);

        return new EmbedBuilder()
            .WithTitle($"🟢 Rétablissement : {safeName}")
            .WithDescription("La cible est à nouveau accessible et répond normalement.")
            .WithColor(Color.Green)
            .AddField("Protocole", protocolStr, inline: true)
            .AddField("Cible", safeHost, inline: true)
            .AddField("Latence", $"{responseTimeMs} ms", inline: true)
            .AddField("Durée de l'incident", FormatDuration(downtime), inline: false)
            .WithFooter("DiscordBot Uptime Monitor")
            .WithTimestamp(timeProvider.GetUtcNow())
            .Build();
    }

    public static string SanitizeUntrustedText(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return input
            .Replace("@everyone", "@\u200beveryone", StringComparison.OrdinalIgnoreCase)
            .Replace("@here", "@\u200bhere", StringComparison.OrdinalIgnoreCase)
            .Replace("<@", "<\u200b@", StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes < 1)
        {
            return $"{Math.Max(1, (int)duration.TotalSeconds)}s";
        }

        if (duration.TotalHours < 1)
        {
            return $"{(int)duration.TotalMinutes}m {duration.Seconds:D2}s";
        }

        return $"{(int)duration.TotalHours}h {duration.Minutes:D2}m {duration.Seconds:D2}s";
    }

    public static string FormatHttpUrl(MonitoredTarget target)
    {
        var hostOrUrl = target.HostOrUrl.Trim();
        if (hostOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            hostOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return hostOrUrl;
        }

        if (target.Port == 443)
        {
            return $"https://{hostOrUrl}";
        }

        if (target.Port.HasValue && target.Port.Value != 80)
        {
            return $"http://{hostOrUrl}:{target.Port.Value}";
        }

        return $"http://{hostOrUrl}";
    }

    private async Task SendAlertEmbedAsync(MonitoredTarget target, Embed embed, CancellationToken ct)
    {
        var channelIdStr = !string.IsNullOrWhiteSpace(target.AlertDiscordChannelId)
            ? target.AlertDiscordChannelId
            : configService.GetSettings().DefaultAlertChannelId;

        if (string.IsNullOrWhiteSpace(channelIdStr)) return;
        if (!ulong.TryParse(channelIdStr, out var channelId)) return;

        try
        {
            if (discordClient.GetChannel(channelId) is IMessageChannel channel)
            {
                await channel.SendMessageAsync(embed: embed, allowedMentions: AllowedMentions.None);
            }
            else
            {
                logger.LogWarning("Salon Discord introuvable pour l'alerte uptime : {ChannelId}", channelId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de l'envoi de l'alerte Discord sur le salon {ChannelId}", channelId);
        }
    }
}
