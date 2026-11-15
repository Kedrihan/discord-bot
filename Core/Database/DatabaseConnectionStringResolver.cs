using Npgsql;

namespace DiscordBot.Core.Database;

public static class DatabaseConnectionStringResolver
{
    public static string Resolve(IConfiguration configuration)
    {
        var raw = configuration.GetConnectionString("BotDb");
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = configuration["DATABASE_URL"] ?? Environment.GetEnvironmentVariable("DATABASE_URL");
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException(
                "Aucune chaîne de connexion PostgreSQL configurée. Définissez DATABASE_URL ou ConnectionStrings:BotDb.");
        }

        var trimmed = raw.Trim();
        if (trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return ConvertPostgresUri(trimmed);
        }

        return trimmed;
    }

    public static string ConvertPostgresUri(string uriString)
    {
        var uri = new Uri(uriString);
        var userInfo = uri.UserInfo.Split(':', 2);
        var username = Uri.UnescapeDataString(userInfo[0]);
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = username,
            Password = password
        };

        var query = uri.Query.TrimStart('?');
        if (!string.IsNullOrEmpty(query))
        {
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                var key = Uri.UnescapeDataString(kv[0]).ToLowerInvariant();
                var val = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : string.Empty;

                if (key == "sslmode" && Enum.TryParse<SslMode>(val, ignoreCase: true, out var sslMode))
                {
                    builder.SslMode = sslMode;
                }
            }
        }

        return builder.ConnectionString;
    }
}
