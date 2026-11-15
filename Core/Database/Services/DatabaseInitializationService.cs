using System.Data;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Core.Database.Services;

public class DatabaseInitializationService(
    IDbContextFactory<BotDbContext> dbContextFactory,
    ILogger<DatabaseInitializationService> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Vérification et initialisation de la base de données SQLite...");

        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await context.Database.EnsureCreatedAsync(cancellationToken);
        await EnsureYouTubeTablesExistAsync(context, cancellationToken);
        await EnsurePermissionsTableExistsAsync(context, cancellationToken);
        await MigrateGuildIsolationSchemaAsync(context, cancellationToken);
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;", cancellationToken);
        await context.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout = 5000;", cancellationToken);

        logger.LogInformation("Base de données SQLite prête.");
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

    private static async Task EnsurePermissionsTableExistsAsync(BotDbContext context, CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS "GuildAdminPermissions" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_GuildAdminPermissions" PRIMARY KEY AUTOINCREMENT,
                "GuildId" TEXT NOT NULL,
                "UserId" TEXT NOT NULL,
                "FeatureFlags" INTEGER NOT NULL,
                "CreatedAt" INTEGER NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_GuildAdminPermissions_GuildId_UserId"
                ON "GuildAdminPermissions" ("GuildId", "UserId");
            """;

        await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }

    private static async Task EnsureYouTubeTablesExistAsync(BotDbContext context, CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS "YouTubeSettings" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_YouTubeSettings" PRIMARY KEY AUTOINCREMENT,
                "IsEnabled" INTEGER NOT NULL,
                "CheckIntervalMinutes" INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS "TrackedYouTubeChannels" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_TrackedYouTubeChannels" PRIMARY KEY AUTOINCREMENT,
                "YouTubeSettingsId" INTEGER NOT NULL,
                "GuildId" TEXT NOT NULL DEFAULT '',
                "ChannelId" TEXT NOT NULL,
                "ChannelTitle" TEXT NOT NULL,
                "ChannelHandle" TEXT NOT NULL,
                "DiscordChannelId" TEXT NOT NULL,
                "CustomMessage" TEXT NOT NULL,
                "IsEnabled" INTEGER NOT NULL,
                CONSTRAINT "FK_TrackedYouTubeChannels_YouTubeSettings_YouTubeSettingsId"
                    FOREIGN KEY ("YouTubeSettingsId") REFERENCES "YouTubeSettings" ("Id") ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS "IX_TrackedYouTubeChannels_ChannelId"
                ON "TrackedYouTubeChannels" ("ChannelId");
            CREATE INDEX IF NOT EXISTS "IX_TrackedYouTubeChannels_YouTubeSettingsId"
                ON "TrackedYouTubeChannels" ("YouTubeSettingsId");

            CREATE TABLE IF NOT EXISTS "YouTubeVideoHistory" (
                "GuildId" TEXT NOT NULL DEFAULT '',
                "VideoId" TEXT NOT NULL,
                "ChannelId" TEXT NOT NULL,
                "DiscoveredAt" INTEGER NOT NULL,
                CONSTRAINT "PK_YouTubeVideoHistory" PRIMARY KEY ("GuildId", "VideoId")
            );

            CREATE INDEX IF NOT EXISTS "IX_YouTubeVideoHistory_DiscoveredAt"
                ON "YouTubeVideoHistory" ("DiscoveredAt");

            CREATE TABLE IF NOT EXISTS "YouTubeInitializedChannels" (
                "GuildId" TEXT NOT NULL DEFAULT '',
                "ChannelId" TEXT NOT NULL,
                "InitializedAt" INTEGER NOT NULL,
                CONSTRAINT "PK_YouTubeInitializedChannels" PRIMARY KEY ("GuildId", "ChannelId")
            );
            """;

        await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }

    private static async Task MigrateGuildIsolationSchemaAsync(BotDbContext context, CancellationToken cancellationToken)
    {
        // 1. TrackedTwitchChannels.GuildId
        if (!await ColumnExistsAsync(context, "TrackedTwitchChannels", "GuildId", cancellationToken))
        {
            await context.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"TrackedTwitchChannels\" ADD COLUMN \"GuildId\" TEXT NOT NULL DEFAULT '';", cancellationToken);
        }

        // 2. TrackedYouTubeChannels.GuildId
        if (!await ColumnExistsAsync(context, "TrackedYouTubeChannels", "GuildId", cancellationToken))
        {
            await context.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"TrackedYouTubeChannels\" ADD COLUMN \"GuildId\" TEXT NOT NULL DEFAULT '';", cancellationToken);
        }

        // 3. TwitchActiveStreams: migrate to composite PK (GuildId, TwitchUsername) if GuildId column missing
        if (!await ColumnExistsAsync(context, "TwitchActiveStreams", "GuildId", cancellationToken))
        {
            const string migrateTwitchActiveStreams = """
                CREATE TABLE IF NOT EXISTS "TwitchActiveStreams_new" (
                    "GuildId" TEXT NOT NULL DEFAULT '',
                    "TwitchUsername" TEXT NOT NULL,
                    "StreamId" TEXT NOT NULL,
                    "DiscordChannelId" TEXT NOT NULL,
                    "DiscordMessageId" INTEGER NOT NULL,
                    "StartedAt" INTEGER NOT NULL,
                    CONSTRAINT "PK_TwitchActiveStreams" PRIMARY KEY ("GuildId", "TwitchUsername")
                );

                INSERT OR IGNORE INTO "TwitchActiveStreams_new" ("GuildId", "TwitchUsername", "StreamId", "DiscordChannelId", "DiscordMessageId", "StartedAt")
                    SELECT '', "TwitchUsername", "StreamId", "DiscordChannelId", "DiscordMessageId", "StartedAt" FROM "TwitchActiveStreams";

                DROP TABLE "TwitchActiveStreams";
                ALTER TABLE "TwitchActiveStreams_new" RENAME TO "TwitchActiveStreams";
                """;
            await context.Database.ExecuteSqlRawAsync(migrateTwitchActiveStreams, cancellationToken);
        }

        // 4. TwitchClips: migrate to composite PK (GuildId, ClipId) if GuildId column missing
        if (!await ColumnExistsAsync(context, "TwitchClips", "GuildId", cancellationToken))
        {
            const string migrateTwitchClips = """
                CREATE TABLE IF NOT EXISTS "TwitchClips_new" (
                    "GuildId" TEXT NOT NULL DEFAULT '',
                    "ClipId" TEXT NOT NULL,
                    "BroadcasterId" TEXT NOT NULL,
                    "DiscoveredAt" INTEGER NOT NULL,
                    CONSTRAINT "PK_TwitchClips" PRIMARY KEY ("GuildId", "ClipId")
                );

                INSERT OR IGNORE INTO "TwitchClips_new" ("GuildId", "ClipId", "BroadcasterId", "DiscoveredAt")
                    SELECT '', "ClipId", "BroadcasterId", "DiscoveredAt" FROM "TwitchClips";

                DROP TABLE "TwitchClips";
                ALTER TABLE "TwitchClips_new" RENAME TO "TwitchClips";

                CREATE INDEX IF NOT EXISTS "IX_TwitchClips_GuildId_BroadcasterId"
                    ON "TwitchClips" ("GuildId", "BroadcasterId");
                CREATE INDEX IF NOT EXISTS "IX_TwitchClips_DiscoveredAt"
                    ON "TwitchClips" ("DiscoveredAt");
                """;
            await context.Database.ExecuteSqlRawAsync(migrateTwitchClips, cancellationToken);
        }

        // 5. TwitchInitializedBroadcasters: migrate to composite PK (GuildId, BroadcasterId) if GuildId column missing
        if (!await ColumnExistsAsync(context, "TwitchInitializedBroadcasters", "GuildId", cancellationToken))
        {
            const string migrateTwitchBroadcasters = """
                CREATE TABLE IF NOT EXISTS "TwitchInitializedBroadcasters_new" (
                    "GuildId" TEXT NOT NULL DEFAULT '',
                    "BroadcasterId" TEXT NOT NULL,
                    "InitializedAt" INTEGER NOT NULL,
                    CONSTRAINT "PK_TwitchInitializedBroadcasters" PRIMARY KEY ("GuildId", "BroadcasterId")
                );

                INSERT OR IGNORE INTO "TwitchInitializedBroadcasters_new" ("GuildId", "BroadcasterId", "InitializedAt")
                    SELECT '', "BroadcasterId", "InitializedAt" FROM "TwitchInitializedBroadcasters";

                DROP TABLE "TwitchInitializedBroadcasters";
                ALTER TABLE "TwitchInitializedBroadcasters_new" RENAME TO "TwitchInitializedBroadcasters";
                """;
            await context.Database.ExecuteSqlRawAsync(migrateTwitchBroadcasters, cancellationToken);
        }

        // 6. YouTubeVideoHistory: migrate to composite PK (GuildId, VideoId) if GuildId column missing
        if (!await ColumnExistsAsync(context, "YouTubeVideoHistory", "GuildId", cancellationToken))
        {
            const string migrateYouTubeVideoHistory = """
                CREATE TABLE IF NOT EXISTS "YouTubeVideoHistory_new" (
                    "GuildId" TEXT NOT NULL DEFAULT '',
                    "VideoId" TEXT NOT NULL,
                    "ChannelId" TEXT NOT NULL,
                    "DiscoveredAt" INTEGER NOT NULL,
                    CONSTRAINT "PK_YouTubeVideoHistory" PRIMARY KEY ("GuildId", "VideoId")
                );

                INSERT OR IGNORE INTO "YouTubeVideoHistory_new" ("GuildId", "VideoId", "ChannelId", "DiscoveredAt")
                    SELECT '', "VideoId", "ChannelId", "DiscoveredAt" FROM "YouTubeVideoHistory";

                DROP TABLE "YouTubeVideoHistory";
                ALTER TABLE "YouTubeVideoHistory_new" RENAME TO "YouTubeVideoHistory";

                CREATE INDEX IF NOT EXISTS "IX_YouTubeVideoHistory_GuildId_ChannelId"
                    ON "YouTubeVideoHistory" ("GuildId", "ChannelId");
                CREATE INDEX IF NOT EXISTS "IX_YouTubeVideoHistory_DiscoveredAt"
                    ON "YouTubeVideoHistory" ("DiscoveredAt");
                """;
            await context.Database.ExecuteSqlRawAsync(migrateYouTubeVideoHistory, cancellationToken);
        }

        // 7. YouTubeInitializedChannels: migrate to composite PK (GuildId, ChannelId) if GuildId column missing
        if (!await ColumnExistsAsync(context, "YouTubeInitializedChannels", "GuildId", cancellationToken))
        {
            const string migrateYouTubeChannels = """
                CREATE TABLE IF NOT EXISTS "YouTubeInitializedChannels_new" (
                    "GuildId" TEXT NOT NULL DEFAULT '',
                    "ChannelId" TEXT NOT NULL,
                    "InitializedAt" INTEGER NOT NULL,
                    CONSTRAINT "PK_YouTubeInitializedChannels" PRIMARY KEY ("GuildId", "ChannelId")
                );

                INSERT OR IGNORE INTO "YouTubeInitializedChannels_new" ("GuildId", "ChannelId", "InitializedAt")
                    SELECT '', "ChannelId", "InitializedAt" FROM "YouTubeInitializedChannels";

                DROP TABLE "YouTubeInitializedChannels";
                ALTER TABLE "YouTubeInitializedChannels_new" RENAME TO "YouTubeInitializedChannels";
                """;
            await context.Database.ExecuteSqlRawAsync(migrateYouTubeChannels, cancellationToken);
        }

        // S'assurer que l'index composite existe même si la table vient d'être créée ou migrée
        await context.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_YouTubeVideoHistory_GuildId_ChannelId\" ON \"YouTubeVideoHistory\" (\"GuildId\", \"ChannelId\");",
            cancellationToken);
    }

    private static async Task<bool> ColumnExistsAsync(BotDbContext context, string tableName, string columnName, CancellationToken ct)
    {
        var connection = context.Database.GetDbConnection();
        var wasClosed = connection.State == ConnectionState.Closed;
        if (wasClosed)
        {
            await context.Database.OpenConnectionAsync(ct);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info(\"{tableName}\");";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var col = reader["name"]?.ToString();
                if (string.Equals(col, columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
        finally
        {
            if (wasClosed)
            {
                await context.Database.CloseConnectionAsync();
            }
        }
    }
}
