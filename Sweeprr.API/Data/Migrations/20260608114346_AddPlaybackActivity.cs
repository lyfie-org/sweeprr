using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sweeprr.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaybackActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlaybackActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MediaServerItemId = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    PlayCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastWatched = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsFinished = table.Column<bool>(type: "INTEGER", nullable: false),
                    ProgressPercent = table.Column<double>(type: "REAL", nullable: false),
                    PlaybackPositionTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaybackActivities", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackActivities_LastWatched",
                table: "PlaybackActivities",
                column: "LastWatched");

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackActivities_MediaServerItemId_UserId",
                table: "PlaybackActivities",
                columns: new[] { "MediaServerItemId", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaybackActivities");
        }
    }
}
