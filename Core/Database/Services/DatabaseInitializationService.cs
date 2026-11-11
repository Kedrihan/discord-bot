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
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;", cancellationToken);
        await context.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout = 5000;", cancellationToken);

        logger.LogInformation("Base de données SQLite prête.");
    }
}
