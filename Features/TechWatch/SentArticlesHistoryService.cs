using DiscordBot.Core.Database;
using DiscordBot.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Services;

public class SentArticlesHistoryService(
    IDbContextFactory<BotDbContext> dbContextFactory,
    TimeProvider timeProvider,
    ILogger<SentArticlesHistoryService> logger)
{
    public void Initialize()
    {
        // Initialisation assurée par DatabaseMigrationService et EF Core
    }

    public bool IsAlreadySent(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        try
        {
            var normalized = NormalizeUrl(url);
            using var context = dbContextFactory.CreateDbContext();
            return context.SentArticles.Any(r => r.Url == normalized);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la vérification de l'article envoyé : {Url}", url);
            return false;
        }
    }

    public async Task MarkAsSentAsync(IEnumerable<string> urls, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();

        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            foreach (var url in urls)
            {
                if (string.IsNullOrWhiteSpace(url)) continue;

                var normalized = NormalizeUrl(url);
                var existing = await context.SentArticles.FindAsync([normalized], cancellationToken);
                if (existing == null)
                {
                    context.SentArticles.Add(new SentArticleEntity
                    {
                        Url = normalized,
                        SentAt = now
                    });
                }
                else
                {
                    existing.SentAt = now;
                }
            }

            // Nettoyage des articles de plus de 30 jours
            var cutoff = now.AddDays(-30);
            var oldArticles = context.SentArticles.Where(r => r.SentAt < cutoff);
            context.SentArticles.RemoveRange(oldArticles);

            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de l'enregistrement de l'historique des articles envoyés.");
        }
    }

    private static string NormalizeUrl(string url) => url.Trim().TrimEnd('/').ToLowerInvariant();
}
