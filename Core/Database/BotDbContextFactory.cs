using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DiscordBot.Core.Database;

/// <summary>
/// Fabrique design-time utilisée par <c>dotnet ef</c> pour générer les migrations PostgreSQL sans démarrer l'application.
/// </summary>
public class BotDbContextFactory : IDesignTimeDbContextFactory<BotDbContext>
{
    public BotDbContext CreateDbContext(string[] args)
    {
        var rawConnectionString = Environment.GetEnvironmentVariable("DATABASE_URL");
        var connectionString = string.IsNullOrWhiteSpace(rawConnectionString)
            ? "Host=localhost;Database=discordbot;Username=postgres;Password=postgres"
            : rawConnectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
              || rawConnectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
                ? DatabaseConnectionStringResolver.ConvertPostgresUri(rawConnectionString.Trim())
                : rawConnectionString.Trim();

        var optionsBuilder = new DbContextOptionsBuilder<BotDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new BotDbContext(optionsBuilder.Options);
    }
}
