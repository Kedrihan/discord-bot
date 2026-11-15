using System.Collections.Concurrent;
using System.Text.Json;
using Discord;
using Discord.WebSocket;
using DiscordBot.Core.Database;
using DiscordBot.Core.Database.Entities;
using DiscordBot.DTO.Embeds;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Core.Discord.Embeds;

public class EmbedTemplateService(
    IDbContextFactory<BotDbContext> dbContextFactory,
    DiscordSocketClient discordClient,
    EmbedTemplateRenderer templateRenderer,
    TimeProvider timeProvider,
    ILogger<EmbedTemplateService> logger)
{
    private readonly ConcurrentDictionary<string, DiscordEmbedTemplateDto> _templateCache = new(StringComparer.OrdinalIgnoreCase);

    public EmbedTemplateRenderer Renderer => templateRenderer;

    public DiscordEmbedTemplateDto GetDefaultTemplate(EmbedTemplateTypeDto type) => type switch
    {
        EmbedTemplateTypeDto.TwitchLive => new DiscordEmbedTemplateDto
        {
            TemplateType = EmbedTemplateTypeDto.TwitchLive,
            ColorHex = "#9146FF",
            TitleTemplate = "{StreamTitle}",
            DescriptionTemplate = string.Empty,
            EnableAuthor = true,
            AuthorNameTemplate = "{StreamerName}",
            FooterTemplate = "Twitch Live Notifications",
            ShowThumbnail = true,
            ShowTimestamp = true,
            CustomFields =
            [
                new("🎮 Jeu / Catégorie", "{GameName}", Inline: true, IsEnabled: true, Order: 0),
                new("👥 Spectateurs", "{ViewerCount}", Inline: true, IsEnabled: true, Order: 1),
                new("⏱️ Début", "<t:{StartedAtUnix}:R>", Inline: true, IsEnabled: true, Order: 2)
            ]
        },
        EmbedTemplateTypeDto.TwitchClip => new DiscordEmbedTemplateDto
        {
            TemplateType = EmbedTemplateTypeDto.TwitchClip,
            ColorHex = "#9146FF",
            TitleTemplate = "{ClipTitle}",
            DescriptionTemplate = string.Empty,
            EnableAuthor = true,
            AuthorNameTemplate = "{BroadcasterName}",
            FooterTemplate = "Twitch Clips",
            ShowThumbnail = true,
            ShowTimestamp = true,
            CustomFields =
            [
                new("🎬 Créé par", "{CreatorName}", Inline: true, IsEnabled: true, Order: 0),
                new("⏱️ Durée", "{DurationSeconds}s", Inline: true, IsEnabled: true, Order: 1),
                new("👥 Vues", "{ViewCount}", Inline: true, IsEnabled: true, Order: 2),
                new("📅 Date", "<t:{CreatedAtUnix}:R>", Inline: true, IsEnabled: true, Order: 3)
            ]
        },
        EmbedTemplateTypeDto.YouTubeVideo => new DiscordEmbedTemplateDto
        {
            TemplateType = EmbedTemplateTypeDto.YouTubeVideo,
            ColorHex = "#FF0000",
            TitleTemplate = "{VideoTitle}",
            DescriptionTemplate = string.Empty,
            EnableAuthor = true,
            AuthorNameTemplate = "{ChannelName}",
            FooterTemplate = "YouTube",
            ShowThumbnail = true,
            ShowTimestamp = true,
            CustomFields =
            [
                new("📺 Type", "{VideoType}", Inline: true, IsEnabled: true, Order: 0),
                new("📅 Publié le", "<t:{PublishedAtUnix}:F>", Inline: true, IsEnabled: true, Order: 1)
            ]
        },
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    public async Task<DiscordEmbedTemplateDto> GetTemplateAsync(
        EmbedTemplateTypeDto type,
        string guildId = "",
        string channelTargetId = "",
        CancellationToken ct = default)
    {
        var typeStr = type.ToString();

        // 1. Channel-specific override
        if (!string.IsNullOrWhiteSpace(channelTargetId) && !string.IsNullOrWhiteSpace(guildId))
        {
            var specificKey = BuildCacheKey(typeStr, guildId, channelTargetId);
            if (_templateCache.TryGetValue(specificKey, out var specificCached)) return specificCached;

            var specificEntity = await FindEntityAsync(typeStr, guildId, channelTargetId, ct);
            if (specificEntity != null)
            {
                var dto = MapEntityToDto(specificEntity);
                _templateCache[specificKey] = dto;
                return dto;
            }
        }

        // 2. Guild-wide template
        if (!string.IsNullOrWhiteSpace(guildId))
        {
            var guildKey = BuildCacheKey(typeStr, guildId, string.Empty);
            if (_templateCache.TryGetValue(guildKey, out var guildCached)) return guildCached;

            var guildEntity = await FindEntityAsync(typeStr, guildId, string.Empty, ct);
            if (guildEntity != null)
            {
                var dto = MapEntityToDto(guildEntity);
                _templateCache[guildKey] = dto;
                return dto;
            }
        }

        // 3. Global template override
        var globalKey = BuildCacheKey(typeStr, string.Empty, string.Empty);
        if (_templateCache.TryGetValue(globalKey, out var globalCached)) return globalCached;

        var globalEntity = await FindEntityAsync(typeStr, string.Empty, string.Empty, ct);
        if (globalEntity != null)
        {
            var dto = MapEntityToDto(globalEntity);
            _templateCache[globalKey] = dto;
            return dto;
        }

        // 4. Default built-in template
        var defaultDto = GetDefaultTemplate(type);
        return defaultDto;
    }

    public async Task<DiscordEmbedTemplateDto> SaveTemplateAsync(
        DiscordEmbedTemplateDto template,
        CancellationToken ct = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(ct);
        var typeStr = template.TemplateType.ToString();
        var guildId = template.GuildId ?? string.Empty;
        var targetId = template.ChannelTargetId ?? string.Empty;

        var entity = await context.DiscordEmbedTemplates.FirstOrDefaultAsync(e =>
            e.TemplateType == typeStr &&
            e.GuildId == guildId &&
            e.ChannelTargetId == targetId, ct);

        var nowUnix = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

        if (entity == null)
        {
            entity = new DiscordEmbedTemplateEntity
            {
                TemplateType = typeStr,
                GuildId = guildId,
                ChannelTargetId = targetId
            };
            context.DiscordEmbedTemplates.Add(entity);
        }

        entity.ColorHex = string.IsNullOrWhiteSpace(template.ColorHex) ? "#9146FF" : template.ColorHex.Trim();
        entity.TitleTemplate = template.TitleTemplate ?? string.Empty;
        entity.DescriptionTemplate = template.DescriptionTemplate ?? string.Empty;
        entity.EnableAuthor = template.EnableAuthor;
        entity.AuthorNameTemplate = template.AuthorNameTemplate ?? string.Empty;
        entity.FooterTemplate = template.FooterTemplate ?? string.Empty;
        entity.ShowThumbnail = template.ShowThumbnail;
        entity.ShowTimestamp = template.ShowTimestamp;
        entity.CustomFieldsJson = JsonSerializer.Serialize(template.CustomFields ?? [], (JsonSerializerOptions?)null);
        entity.UpdatedAt = nowUnix;

        await context.SaveChangesAsync(ct);

        var savedDto = MapEntityToDto(entity);
        var cacheKey = BuildCacheKey(typeStr, guildId, targetId);
        _templateCache[cacheKey] = savedDto;

        logger.LogInformation("Modèle d'embed sauvegardé : Type={Type}, Guild={GuildId}, Target={TargetId}",
            typeStr, guildId, targetId);

        return savedDto;
    }

    public async Task<bool> ResetTemplateAsync(
        EmbedTemplateTypeDto type,
        string guildId = "",
        string channelTargetId = "",
        CancellationToken ct = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(ct);
        var typeStr = type.ToString();
        var gId = guildId ?? string.Empty;
        var tId = channelTargetId ?? string.Empty;

        var entity = await context.DiscordEmbedTemplates.FirstOrDefaultAsync(e =>
            e.TemplateType == typeStr &&
            e.GuildId == gId &&
            e.ChannelTargetId == tId, ct);

        if (entity == null) return false;

        context.DiscordEmbedTemplates.Remove(entity);
        await context.SaveChangesAsync(ct);

        var cacheKey = BuildCacheKey(typeStr, gId, tId);
        _templateCache.TryRemove(cacheKey, out _);

        logger.LogInformation("Modèle d'embed réinitialisé : Type={Type}, Guild={GuildId}, Target={TargetId}",
            typeStr, gId, tId);

        return true;
    }

    public async Task<IReadOnlyList<DiscordEmbedTemplateDto>> GetAllTemplatesAsync(
        string? guildId = null,
        CancellationToken ct = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(ct);
        var query = context.DiscordEmbedTemplates.AsNoTracking();

        if (guildId != null)
        {
            query = query.Where(e => e.GuildId == guildId || e.GuildId == string.Empty);
        }

        var entities = await query.ToListAsync(ct);
        return entities.Select(MapEntityToDto).ToList();
    }

    public IReadOnlyList<EmbedVariableInfoDto> GetAvailableVariables(EmbedTemplateTypeDto type) => type switch
    {
        EmbedTemplateTypeDto.TwitchLive =>
        [
            new("{StreamTitle}", "Titre du live en cours", "Live chill & code en C# .NET 10"),
            new("{StreamerName}", "Nom d'affichage du streamer", "MonStreamer"),
            new("{StreamerLogin}", "Identifiant Twitch (login)", "monstreamer"),
            new("{GameName}", "Jeu ou catégorie", "Software and Game Development"),
            new("{ViewerCount}", "Nombre actuel de spectateurs", "1 250"),
            new("{StreamUrl}", "URL de la chaîne Twitch", "https://twitch.tv/monstreamer"),
            new("{StartedAtUnix}", "Horodatage Unix du début de stream (ex: <t:{StartedAtUnix}:R>)", "1731664800"),
            new("{StartedAtFormatted}", "Date et heure du lancement du stream", "15/11/2026 10:00")
        ],
        EmbedTemplateTypeDto.TwitchClip =>
        [
            new("{ClipTitle}", "Titre du clip", "Un move légendaire !"),
            new("{BroadcasterName}", "Nom du streamer clipsé", "MonStreamer"),
            new("{CreatorName}", "Auteur / créateur du clip", "ClipMaster42"),
            new("{ClipUrl}", "Lien direct vers le clip", "https://clips.twitch.tv/SampleClip123"),
            new("{DurationSeconds}", "Durée du clip en secondes", "30"),
            new("{ViewCount}", "Nombre de vues du clip", "450"),
            new("{CreatedAtUnix}", "Horodatage Unix de création (ex: <t:{CreatedAtUnix}:R>)", "1731664800"),
            new("{CreatedAtFormatted}", "Date et heure de création du clip", "15/11/2026 10:00")
        ],
        EmbedTemplateTypeDto.YouTubeVideo =>
        [
            new("{VideoTitle}", "Titre de la vidéo ou du Short", "Nouvelles fonctionnalités de C# 13"),
            new("{ChannelName}", "Nom de la chaîne YouTube", "DotNet Dev"),
            new("{ChannelId}", "Identifiant de la chaîne YouTube", "UC_x5XG1OV2P6uZZ5FSM9Ttw"),
            new("{VideoId}", "Identifiant unique de la vidéo", "dQw4w9WgXcQ"),
            new("{VideoUrl}", "Lien direct vers la vidéo YouTube", "https://www.youtube.com/watch?v=dQw4w9WgXcQ"),
            new("{VideoType}", "Type de contenu ('Vidéo' ou 'Short')", "Vidéo"),
            new("{PublishedAtUnix}", "Horodatage Unix de publication (ex: <t:{PublishedAtUnix}:F>)", "1731664800"),
            new("{PublishedAtFormatted}", "Date et heure de publication", "15/11/2026 10:00")
        ],
        _ => []
    };

    public (Dictionary<string, string> Variables, string? AuthorIconUrl, string? AuthorUrl, string? EmbedUrl, string? ImageUrl, DateTimeOffset Timestamp) GetSampleData(EmbedTemplateTypeDto type)
    {
        var now = timeProvider.GetUtcNow();
        var nowUnix = now.ToUnixTimeSeconds().ToString();
        var nowFormatted = now.ToString("g");

        return type switch
        {
            EmbedTemplateTypeDto.TwitchLive => (
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["StreamTitle"] = "Session de dev C# .NET 10 & Blazor en direct !",
                    ["Streamer"] = "AlexDev",
                    ["StreamerName"] = "AlexDev",
                    ["StreamerLogin"] = "alexdev",
                    ["GameName"] = "Software and Game Development",
                    ["ViewerCount"] = "1 420",
                    ["StreamUrl"] = "https://twitch.tv/alexdev",
                    ["StartedAtUnix"] = nowUnix,
                    ["StartedAtFormatted"] = nowFormatted
                },
                "https://static-cdn.jtvnw.net/jtv_user_pictures/sample-profile_image-300x300.png",
                "https://twitch.tv/alexdev",
                "https://twitch.tv/alexdev",
                "https://static-cdn.jtvnw.net/previews-ttv/live_user_alexdev-1280x720.jpg",
                now
            ),
            EmbedTemplateTypeDto.TwitchClip => (
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ClipTitle"] = "Action incroyable en tournoi !",
                    ["BroadcasterName"] = "AlexDev",
                    ["Streamer"] = "AlexDev",
                    ["CreatorName"] = "ViewerPro",
                    ["ClipUrl"] = "https://clips.twitch.tv/SampleClip",
                    ["DurationSeconds"] = "28",
                    ["ViewCount"] = "890",
                    ["CreatedAtUnix"] = nowUnix,
                    ["CreatedAtFormatted"] = nowFormatted
                },
                null,
                "https://twitch.tv/alexdev",
                "https://clips.twitch.tv/SampleClip",
                "https://clips-media-assets2.twitch.tv/sample-preview-480x272.jpg",
                now
            ),
            EmbedTemplateTypeDto.YouTubeVideo => (
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["VideoTitle"] = "Découverte des nouveautés de .NET 10 & C# 13",
                    ["ChannelName"] = "DotNet Tech",
                    ["ChannelId"] = "UC_x5XG1OV2P6uZZ5FSM9Ttw",
                    ["VideoId"] = "sample12345",
                    ["VideoUrl"] = "https://www.youtube.com/watch?v=sample12345",
                    ["VideoType"] = "Vidéo",
                    ["PublishedAtUnix"] = nowUnix,
                    ["PublishedAtFormatted"] = nowFormatted
                },
                null,
                "https://www.youtube.com/channel/UC_x5XG1OV2P6uZZ5FSM9Ttw",
                "https://www.youtube.com/watch?v=sample12345",
                "https://i.ytimg.com/vi/sample12345/hqdefault.jpg",
                now
            ),
            _ => ([], null, null, null, null, now)
        };
    }

    public async Task<EmbedTestResultDto> SendTestEmbedAsync(
        EmbedTemplateTypeDto type,
        DiscordEmbedTemplateDto template,
        string discordChannelId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(discordChannelId) || !ulong.TryParse(discordChannelId, out var channelId))
        {
            return new EmbedTestResultDto(false, "Veuillez renseigner un ID de salon Discord valide.");
        }

        var channel = discordClient.GetChannel(channelId) as IMessageChannel;
        if (channel == null)
        {
            return new EmbedTestResultDto(false, $"Salon Discord introuvable ou inaccessible par le bot ({discordChannelId}).");
        }

        if (!string.IsNullOrWhiteSpace(template.GuildId) &&
            channel is SocketGuildChannel sgc &&
            !string.Equals(sgc.Guild.Id.ToString(), template.GuildId.Trim(), StringComparison.Ordinal))
        {
            return new EmbedTestResultDto(false, "Le salon Discord sélectionné n'appartient pas au serveur configuré pour ce modèle.");
        }

        try
        {
            var sample = GetSampleData(type);
            var embed = templateRenderer.RenderEmbed(
                template,
                sample.Variables,
                authorIconUrl: sample.AuthorIconUrl,
                authorUrl: sample.AuthorUrl,
                embedUrl: sample.EmbedUrl,
                imageUrl: sample.ImageUrl,
                timestamp: sample.Timestamp);

            await channel.SendMessageAsync(
                text: "🧪 **[Test] Prévisualisation du modèle d'embed personnalisé**",
                embed: embed,
                allowedMentions: AllowedMentions.None);

            logger.LogInformation("Embed de test ({Type}) envoyé avec succès dans le salon {ChannelId}", type, discordChannelId);
            return new EmbedTestResultDto(true, $"Embed de test envoyé avec succès dans le salon <#{discordChannelId}> !");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de l'envoi de l'embed de test dans le salon {ChannelId}", discordChannelId);
            return new EmbedTestResultDto(false, $"Erreur lors de l'envoi de l'embed de test : {ex.Message}");
        }
    }

    private async Task<DiscordEmbedTemplateEntity?> FindEntityAsync(
        string typeStr,
        string guildId,
        string channelTargetId,
        CancellationToken ct)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(ct);
        return await context.DiscordEmbedTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(e =>
                e.TemplateType == typeStr &&
                e.GuildId == guildId &&
                e.ChannelTargetId == channelTargetId, ct);
    }

    private static string BuildCacheKey(string type, string guildId, string targetId) =>
        $"{type}::{guildId}::{targetId}";

    private static DiscordEmbedTemplateDto MapEntityToDto(DiscordEmbedTemplateEntity entity)
    {
        var templateType = Enum.TryParse<EmbedTemplateTypeDto>(entity.TemplateType, true, out var t)
            ? t
            : EmbedTemplateTypeDto.TwitchLive;

        List<DiscordEmbedFieldTemplateDto> fields = [];
        if (!string.IsNullOrWhiteSpace(entity.CustomFieldsJson))
        {
            try
            {
                fields = JsonSerializer.Deserialize<List<DiscordEmbedFieldTemplateDto>>(entity.CustomFieldsJson, (JsonSerializerOptions?)null) ?? [];
            }
            catch
            {
                fields = [];
            }
        }

        return new DiscordEmbedTemplateDto
        {
            Id = entity.Id,
            TemplateType = templateType,
            GuildId = entity.GuildId,
            ChannelTargetId = entity.ChannelTargetId,
            ColorHex = entity.ColorHex,
            TitleTemplate = entity.TitleTemplate,
            DescriptionTemplate = entity.DescriptionTemplate,
            EnableAuthor = entity.EnableAuthor,
            AuthorNameTemplate = entity.AuthorNameTemplate,
            FooterTemplate = entity.FooterTemplate,
            ShowThumbnail = entity.ShowThumbnail,
            ShowTimestamp = entity.ShowTimestamp,
            CustomFields = fields,
            UpdatedAt = entity.UpdatedAt > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(entity.UpdatedAt) : DateTimeOffset.MinValue
        };
    }
}
