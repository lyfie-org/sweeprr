using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sweeprr.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGenresAndResolutionToSweepItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Genres",
                table: "SweepItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResolutionHeight",
                table: "SweepItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VideoCodec",
                table: "SweepItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AudioChannels",
                table: "SweepItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlaybackHistoryRetentionDays",
                table: "GlobalSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 365);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Genres",
                table: "SweepItems");

            migrationBuilder.DropColumn(
                name: "ResolutionHeight",
                table: "SweepItems");

            migrationBuilder.DropColumn(
                name: "VideoCodec",
                table: "SweepItems");

            migrationBuilder.DropColumn(
                name: "AudioChannels",
                table: "SweepItems");

            migrationBuilder.DropColumn(
                name: "PlaybackHistoryRetentionDays",
                table: "GlobalSettings");
        }
    }
}
