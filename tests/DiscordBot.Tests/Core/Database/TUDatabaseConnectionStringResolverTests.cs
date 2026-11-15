using DiscordBot.Core.Database;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace DiscordBot.Tests.Core.Database;

[TestFixture]
public class TUDatabaseConnectionStringResolverTests
{
    [Test]
    public void Resolve_ShouldThrowInvalidOperationException_WhenNoConfigOrEnvIsSet()
    {
        var config = new ConfigurationBuilder().Build();

        var act = () => DatabaseConnectionStringResolver.Resolve(config);

        act.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void Resolve_ShouldConvertPostgresUri_WhenDatabaseUrlUriIsProvided()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DATABASE_URL"] = "postgresql://myuser:p%40ssword@ep-cool-host.eu-central-1.aws.neon.tech:5432/neondb?sslmode=require"
            })
            .Build();

        var connectionString = DatabaseConnectionStringResolver.Resolve(config);

        connectionString.Should().Contain("Host=ep-cool-host.eu-central-1.aws.neon.tech");
        connectionString.Should().Contain("Port=5432");
        connectionString.Should().Contain("Database=neondb");
        connectionString.Should().Contain("Username=myuser");
        connectionString.Should().Contain("Password=p@ssword");
        connectionString.Should().Contain("SSL Mode=Require");
    }

    [Test]
    public void Resolve_ShouldReturnNpgsqlConnectionString_WhenConnectionStringIsProvided()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BotDb"] = "Host=localhost;Database=discordbot;Username=postgres;Password=secret"
            })
            .Build();

        var connectionString = DatabaseConnectionStringResolver.Resolve(config);

        connectionString.Should().Be("Host=localhost;Database=discordbot;Username=postgres;Password=secret");
    }
}
