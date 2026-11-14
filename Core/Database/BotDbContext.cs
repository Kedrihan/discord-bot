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
    public DbSet<YouTubeSettingsEntity> YouTubeSettings => Set<YouTubeSettingsEntity>();
    public DbSet<TrackedYouTubeChannelEntity> TrackedYouTubeChannels => Set<TrackedYouTubeChannelEntity>();
    public DbSet<YouTubeVideoHistoryEntity> YouTubeVideoHistory => Set<YouTubeVideoHistoryEntity>();
    public DbSet<YouTubeInitializedChannelEntity> YouTubeInitializedChannels => Set<YouTubeInitializedChannelEntity>();
    public DbSet<GuildAdminPermissionEntity> GuildAdminPermissions => Set<GuildAdminPermissionEntity>();

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
            entity.HasIndex(e => new { e.GuildId, e.TwitchUsername });
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
            entity.HasKey(e => new { e.GuildId, e.ClipId });
            entity.HasIndex(e => new { e.GuildId, e.BroadcasterId });
            entity.HasIndex(e => e.DiscoveredAt);
            entity.Property(e => e.DiscoveredAt)
                .HasConversion(
                    v => v.ToUnixTimeMilliseconds(),
                    v => DateTimeOffset.FromUnixTimeMilliseconds(v));
        });

        // TwitchInitializedBroadcasters
        modelBuilder.Entity<TwitchInitializedBroadcasterEntity>(entity =>
        {
            entity.HasKey(e => new { e.GuildId, e.BroadcasterId });
            entity.Property(e => e.InitializedAt)
                .HasConversion(
                    v => v.ToUnixTimeMilliseconds(),
                    v => DateTimeOffset.FromUnixTimeMilliseconds(v));
        });

        // TwitchActiveStreams
        modelBuilder.Entity<TwitchActiveStreamEntity>(entity =>
        {
            entity.HasKey(e => new { e.GuildId, e.TwitchUsername });
            entity.Property(e => e.StartedAt)
                .HasConversion(
                    v => v.ToUnixTimeMilliseconds(),
                    v => DateTimeOffset.FromUnixTimeMilliseconds(v));
        });

        // YouTubeSettings & TrackedYouTubeChannels
        modelBuilder.Entity<YouTubeSettingsEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasMany(e => e.Channels)
                .WithOne(c => c.YouTubeSettings)
                .HasForeignKey(c => c.YouTubeSettingsId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TrackedYouTubeChannelEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.GuildId, e.ChannelId });
        });

        // YouTubeVideoHistory
        modelBuilder.Entity<YouTubeVideoHistoryEntity>(entity =>
        {
            entity.HasKey(e => new { e.GuildId, e.VideoId });
            entity.HasIndex(e => new { e.GuildId, e.ChannelId });
            entity.HasIndex(e => e.DiscoveredAt);
            entity.Property(e => e.DiscoveredAt)
                .HasConversion(
                    v => v.ToUnixTimeMilliseconds(),
                    v => DateTimeOffset.FromUnixTimeMilliseconds(v));
        });

        // YouTubeInitializedChannels
        modelBuilder.Entity<YouTubeInitializedChannelEntity>(entity =>
        {
            entity.HasKey(e => new { e.GuildId, e.ChannelId });
            entity.Property(e => e.InitializedAt)
                .HasConversion(
                    v => v.ToUnixTimeMilliseconds(),
                    v => DateTimeOffset.FromUnixTimeMilliseconds(v));
        });

        // GuildAdminPermissions
        modelBuilder.Entity<GuildAdminPermissionEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.GuildId, e.UserId }).IsUnique();
            entity.Property(e => e.CreatedAt)
                .HasConversion(
                    v => v.ToUnixTimeMilliseconds(),
                    v => DateTimeOffset.FromUnixTimeMilliseconds(v));
        });
    }
}
