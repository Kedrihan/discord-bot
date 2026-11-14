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
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;", cancellationToken);
        await context.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout = 5000;", cancellationToken);

        logger.LogInformation("Base de données SQLite prête.");
    }

    /// <summary>
    /// Crée les tables YouTube si la base SQLite existait déjà avant l'ajout de la fonctionnalité
    /// (EnsureCreatedAsync ne modifie pas une base existante).
    /// </summary>
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
                "VideoId" TEXT NOT NULL CONSTRAINT "PK_YouTubeVideoHistory" PRIMARY KEY,
                "ChannelId" TEXT NOT NULL,
                "DiscoveredAt" INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS "IX_YouTubeVideoHistory_ChannelId"
                ON "YouTubeVideoHistory" ("ChannelId");
            CREATE INDEX IF NOT EXISTS "IX_YouTubeVideoHistory_DiscoveredAt"
                ON "YouTubeVideoHistory" ("DiscoveredAt");

            CREATE TABLE IF NOT EXISTS "YouTubeInitializedChannels" (
                "ChannelId" TEXT NOT NULL CONSTRAINT "PK_YouTubeInitializedChannels" PRIMARY KEY,
                "InitializedAt" INTEGER NOT NULL
            );
            """;

        await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }
}
