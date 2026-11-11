using DiscordBot.Core.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Tests.TestHelpers;

public sealed class TestBotDbContextFactory : IDbContextFactory<BotDbContext>, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<BotDbContext> _options;

    public TestBotDbContextFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<BotDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new BotDbContext(_options);
        context.Database.EnsureCreated();
    }

    public BotDbContext CreateDbContext() => new(_options);

    public Task<BotDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new BotDbContext(_options));

    public void Dispose()
    {
        _connection.Dispose();
    }
}
