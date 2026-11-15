using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DiscordBot.Core.Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DiscordEmbedTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TemplateType = table.Column<string>(type: "text", nullable: false),
                    GuildId = table.Column<string>(type: "text", nullable: false),
                    ChannelTargetId = table.Column<string>(type: "text", nullable: false),
                    ColorHex = table.Column<string>(type: "text", nullable: false),
                    TitleTemplate = table.Column<string>(type: "text", nullable: false),
                    DescriptionTemplate = table.Column<string>(type: "text", nullable: false),
                    EnableAuthor = table.Column<bool>(type: "boolean", nullable: false),
                    AuthorNameTemplate = table.Column<string>(type: "text", nullable: false),
                    FooterTemplate = table.Column<string>(type: "text", nullable: false),
                    ShowThumbnail = table.Column<bool>(type: "boolean", nullable: false),
                    ShowTimestamp = table.Column<bool>(type: "boolean", nullable: false),
                    CustomFieldsJson = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscordEmbedTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GuildAdminPermissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GuildId = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    FeatureFlags = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuildAdminPermissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MorningBriefingSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ScheduleDays = table.Column<string>(type: "text", nullable: false),
                    ScheduleTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    DestinationType = table.Column<int>(type: "integer", nullable: false),
                    TargetId = table.Column<string>(type: "text", nullable: false),
                    Address = table.Column<string>(type: "text", nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    MorningStartHour = table.Column<int>(type: "integer", nullable: false),
                    MorningEndHour = table.Column<int>(type: "integer", nullable: false),
                    EveningStartHour = table.Column<int>(type: "integer", nullable: false),
                    EveningEndHour = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MorningBriefingSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SentArticles",
                columns: table => new
                {
                    Url = table.Column<string>(type: "text", nullable: false),
                    SentAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentArticles", x => x.Url);
                });

            migrationBuilder.CreateTable(
                name: "TechWatchSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ScheduleDays = table.Column<string>(type: "text", nullable: false),
                    ScheduleTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    DestinationType = table.Column<int>(type: "integer", nullable: false),
                    TargetId = table.Column<string>(type: "text", nullable: false),
                    Topics = table.Column<string>(type: "text", nullable: false),
                    EnableDevTo = table.Column<bool>(type: "boolean", nullable: false),
                    EnableHackerNews = table.Column<bool>(type: "boolean", nullable: false),
                    EnableDotnetBlogRss = table.Column<bool>(type: "boolean", nullable: false),
                    CustomRssFeeds = table.Column<string>(type: "text", nullable: false),
                    GeminiModel = table.Column<string>(type: "text", nullable: false),
                    MaxArticles = table.Column<int>(type: "integer", nullable: false),
                    TranslateToFrench = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TechWatchSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TwitchActiveStreams",
                columns: table => new
                {
                    GuildId = table.Column<string>(type: "text", nullable: false),
                    TwitchUsername = table.Column<string>(type: "text", nullable: false),
                    StreamId = table.Column<string>(type: "text", nullable: false),
                    DiscordChannelId = table.Column<string>(type: "text", nullable: false),
                    DiscordMessageId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    StartedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TwitchActiveStreams", x => new { x.GuildId, x.TwitchUsername });
                });

            migrationBuilder.CreateTable(
                name: "TwitchClips",
                columns: table => new
                {
                    GuildId = table.Column<string>(type: "text", nullable: false),
                    ClipId = table.Column<string>(type: "text", nullable: false),
                    BroadcasterId = table.Column<string>(type: "text", nullable: false),
                    DiscoveredAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TwitchClips", x => new { x.GuildId, x.ClipId });
                });

            migrationBuilder.CreateTable(
                name: "TwitchInitializedBroadcasters",
                columns: table => new
                {
                    GuildId = table.Column<string>(type: "text", nullable: false),
                    BroadcasterId = table.Column<string>(type: "text", nullable: false),
                    InitializedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TwitchInitializedBroadcasters", x => new { x.GuildId, x.BroadcasterId });
                });

            migrationBuilder.CreateTable(
                name: "TwitchSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsClipsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CheckIntervalSeconds = table.Column<int>(type: "integer", nullable: false),
                    ClipCheckIntervalSeconds = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TwitchSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "YouTubeInitializedChannels",
                columns: table => new
                {
                    GuildId = table.Column<string>(type: "text", nullable: false),
                    ChannelId = table.Column<string>(type: "text", nullable: false),
                    InitializedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YouTubeInitializedChannels", x => new { x.GuildId, x.ChannelId });
                });

            migrationBuilder.CreateTable(
                name: "YouTubeSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CheckIntervalMinutes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YouTubeSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "YouTubeVideoHistory",
                columns: table => new
                {
                    GuildId = table.Column<string>(type: "text", nullable: false),
                    VideoId = table.Column<string>(type: "text", nullable: false),
                    ChannelId = table.Column<string>(type: "text", nullable: false),
                    DiscoveredAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YouTubeVideoHistory", x => new { x.GuildId, x.VideoId });
                });

            migrationBuilder.CreateTable(
                name: "TrackedTwitchChannels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TwitchSettingsId = table.Column<int>(type: "integer", nullable: false),
                    GuildId = table.Column<string>(type: "text", nullable: false),
                    TwitchUsername = table.Column<string>(type: "text", nullable: false),
                    DiscordChannelId = table.Column<string>(type: "text", nullable: false),
                    CustomMessage = table.Column<string>(type: "text", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsClipEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ClipDiscordChannelId = table.Column<string>(type: "text", nullable: false),
                    ClipCustomMessage = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedTwitchChannels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackedTwitchChannels_TwitchSettings_TwitchSettingsId",
                        column: x => x.TwitchSettingsId,
                        principalTable: "TwitchSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrackedYouTubeChannels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    YouTubeSettingsId = table.Column<int>(type: "integer", nullable: false),
                    GuildId = table.Column<string>(type: "text", nullable: false),
                    ChannelId = table.Column<string>(type: "text", nullable: false),
                    ChannelTitle = table.Column<string>(type: "text", nullable: false),
                    ChannelHandle = table.Column<string>(type: "text", nullable: false),
                    DiscordChannelId = table.Column<string>(type: "text", nullable: false),
                    CustomMessage = table.Column<string>(type: "text", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedYouTubeChannels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackedYouTubeChannels_YouTubeSettings_YouTubeSettingsId",
                        column: x => x.YouTubeSettingsId,
                        principalTable: "YouTubeSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiscordEmbedTemplates_TemplateType_GuildId_ChannelTargetId",
                table: "DiscordEmbedTemplates",
                columns: new[] { "TemplateType", "GuildId", "ChannelTargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuildAdminPermissions_GuildId_UserId",
                table: "GuildAdminPermissions",
                columns: new[] { "GuildId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SentArticles_SentAt",
                table: "SentArticles",
                column: "SentAt");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedTwitchChannels_GuildId_TwitchUsername",
                table: "TrackedTwitchChannels",
                columns: new[] { "GuildId", "TwitchUsername" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedTwitchChannels_TwitchSettingsId",
                table: "TrackedTwitchChannels",
                column: "TwitchSettingsId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedYouTubeChannels_GuildId_ChannelId",
                table: "TrackedYouTubeChannels",
                columns: new[] { "GuildId", "ChannelId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedYouTubeChannels_YouTubeSettingsId",
                table: "TrackedYouTubeChannels",
                column: "YouTubeSettingsId");

            migrationBuilder.CreateIndex(
                name: "IX_TwitchClips_DiscoveredAt",
                table: "TwitchClips",
                column: "DiscoveredAt");

            migrationBuilder.CreateIndex(
                name: "IX_TwitchClips_GuildId_BroadcasterId",
                table: "TwitchClips",
                columns: new[] { "GuildId", "BroadcasterId" });

            migrationBuilder.CreateIndex(
                name: "IX_YouTubeVideoHistory_DiscoveredAt",
                table: "YouTubeVideoHistory",
                column: "DiscoveredAt");

            migrationBuilder.CreateIndex(
                name: "IX_YouTubeVideoHistory_GuildId_ChannelId",
                table: "YouTubeVideoHistory",
                columns: new[] { "GuildId", "ChannelId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiscordEmbedTemplates");

            migrationBuilder.DropTable(
                name: "GuildAdminPermissions");

            migrationBuilder.DropTable(
                name: "MorningBriefingSettings");

            migrationBuilder.DropTable(
                name: "SentArticles");

            migrationBuilder.DropTable(
                name: "TechWatchSettings");

            migrationBuilder.DropTable(
                name: "TrackedTwitchChannels");

            migrationBuilder.DropTable(
                name: "TrackedYouTubeChannels");

            migrationBuilder.DropTable(
                name: "TwitchActiveStreams");

            migrationBuilder.DropTable(
                name: "TwitchClips");

            migrationBuilder.DropTable(
                name: "TwitchInitializedBroadcasters");

            migrationBuilder.DropTable(
                name: "YouTubeInitializedChannels");

            migrationBuilder.DropTable(
                name: "YouTubeVideoHistory");

            migrationBuilder.DropTable(
                name: "TwitchSettings");

            migrationBuilder.DropTable(
                name: "YouTubeSettings");
        }
    }
}
