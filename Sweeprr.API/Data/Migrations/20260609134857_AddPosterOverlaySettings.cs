using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sweeprr.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPosterOverlaySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PosterBackupDir",
                table: "GlobalSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "/config/poster-backups");

            migrationBuilder.AddColumn<bool>(
                name: "PosterOverlaysEnabled",
                table: "GlobalSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PosterBackupDir",
                table: "GlobalSettings");

            migrationBuilder.DropColumn(
                name: "PosterOverlaysEnabled",
                table: "GlobalSettings");
        }
    }
}
