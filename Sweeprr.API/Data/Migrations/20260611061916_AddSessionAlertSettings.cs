using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sweeprr.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionAlertSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "JellyfinSessionAlertsEnabled",
                table: "GlobalSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "PreSweepBroadcastEnabled",
                table: "GlobalSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "JellyfinSessionAlertsEnabled",
                table: "GlobalSettings");

            migrationBuilder.DropColumn(
                name: "PreSweepBroadcastEnabled",
                table: "GlobalSettings");
        }
    }
}
