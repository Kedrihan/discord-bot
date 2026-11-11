using System.Text.Json;
using DiscordBot.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Core.Database;

public class BotDbContext(DbContextOptions<BotDbContext> options) : DbContext(options)
{
    public DbSet<MorningBriefingSettingsEntity> MorningBriefingSettings => Set<MorningBriefingSettingsEntity>();
    public DbSet<TechWatchSettingsEntity> TechWatchSettings => Set<TechWatchSettingsEntity>();
    public DbSet<TwitchSettingsEntity> TwitchSettings => Set<TwitchSettingsEntity>();
    public DbSet<TrackedTwitchChannelEntity> TrackedTwitchChannels => Set<TrackedTwitchChannelEntity>();
    public DbSet<SentArticleEntity> SentArticles => Set<SentArticleEntity>();
    public DbSet<TwitchClipHistoryEntity> TwitchClips => Set<TwitchClipHistoryEntity>();
    public DbSet<TwitchInitializedBroadcasterEntity> TwitchInitializedBroadcasters => Set<TwitchInitializedBroadcasterEntity>();
    public DbSet<TwitchActiveStreamEntity> TwitchActiveStreams => Set<TwitchActiveStreamEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // MorningBriefingSettings
        modelBuilder.Entity<MorningBriefingSettingsEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ScheduleDays)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<DayOfWeek>>(v, (JsonSerializerOptions?)null) ?? new List<DayOfWeek>());
        });

        // TechWatchSettings
        modelBuilder.Entity<TechWatchSettingsEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ScheduleDays)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<DayOfWeek>>(v, (JsonSerializerOptions?)null) ?? new List<DayOfWeek>());
            entity.Property(e => e.Topics)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());
            entity.Property(e => e.CustomRssFeeds)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());
        });

        // TwitchSettings & TrackedTwitchChannels
        modelBuilder.Entity<TwitchSettingsEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasMany(e => e.Channels)
                .WithOne(c => c.TwitchSettings)
                .HasForeignKey(c => c.TwitchSettingsId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TrackedTwitchChannelEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TwitchUsername);
        });

        // SentArticles
        modelBuilder.Entity<SentArticleEntity>(entity =>
        {
            entity.HasKey(e => e.Url);
            entity.HasIndex(e => e.SentAt);
            entity.Property(e => e.SentAt)
                .HasConversion(
                    v => v.ToUnixTimeMilliseconds(),
                    v => DateTimeOffset.FromUnixTimeMilliseconds(v));
        });

        // TwitchClips
        modelBuilder.Entity<TwitchClipHistoryEntity>(entity =>
        {
            entity.HasKey(e => e.ClipId);
            entity.HasIndex(e => e.BroadcasterId);
            entity.HasIndex(e => e.DiscoveredAt);
            entity.Property(e => e.DiscoveredAt)
                .HasConversion(
                    v => v.ToUnixTimeMilliseconds(),
                    v => DateTimeOffset.FromUnixTimeMilliseconds(v));
        });

        // TwitchInitializedBroadcasters
        modelBuilder.Entity<TwitchInitializedBroadcasterEntity>(entity =>
        {
            entity.HasKey(e => e.BroadcasterId);
            entity.Property(e => e.InitializedAt)
                .HasConversion(
                    v => v.ToUnixTimeMilliseconds(),
                    v => DateTimeOffset.FromUnixTimeMilliseconds(v));
        });

        // TwitchActiveStreams
        modelBuilder.Entity<TwitchActiveStreamEntity>(entity =>
        {
            entity.HasKey(e => e.TwitchUsername);
            entity.Property(e => e.StartedAt)
                .HasConversion(
                    v => v.ToUnixTimeMilliseconds(),
                    v => DateTimeOffset.FromUnixTimeMilliseconds(v));
        });
    }
}
