using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Core.Database.Services;

public class DatabaseInitializationService(
    IDbContextFactory<BotDbContext> dbContextFactory,
    ILogger<DatabaseInitializationService> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Vérification et application des migrations PostgreSQL...");

        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);

        logger.LogInformation("Base de données PostgreSQL prête.");
    }

    public async Task BackfillGuildIdsAsync(DiscordSocketClient discordClient, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var updated = false;

            var twitchChannels = await context.TrackedTwitchChannels
                .Where(c => string.IsNullOrEmpty(c.GuildId))
                .ToListAsync(cancellationToken);

            foreach (var ch in twitchChannels)
            {
                if (ulong.TryParse(ch.DiscordChannelId, out var channelId) &&
                    discordClient.GetChannel(channelId) is SocketGuildChannel sgc)
                {
                    ch.GuildId = sgc.Guild.Id.ToString();
                    updated = true;
                    logger.LogInformation("Backfill GuildId={GuildId} pour chaîne Twitch={Username} (Salon={ChannelId})",
                        ch.GuildId, ch.TwitchUsername, ch.DiscordChannelId);
                }
            }

            var youtubeChannels = await context.TrackedYouTubeChannels
                .Where(c => string.IsNullOrEmpty(c.GuildId))
                .ToListAsync(cancellationToken);

            foreach (var ch in youtubeChannels)
            {
                if (ulong.TryParse(ch.DiscordChannelId, out var channelId) &&
                    discordClient.GetChannel(channelId) is SocketGuildChannel sgc)
                {
                    ch.GuildId = sgc.Guild.Id.ToString();
                    updated = true;
                    logger.LogInformation("Backfill GuildId={GuildId} pour chaîne YouTube={ChannelId} (Salon={DiscordChannelId})",
                        ch.GuildId, ch.ChannelId, ch.DiscordChannelId);
                }
            }

            if (updated)
            {
                await context.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Backfill des GuildId terminé avec succès.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors du backfill des GuildId.");
        }
    }
}
