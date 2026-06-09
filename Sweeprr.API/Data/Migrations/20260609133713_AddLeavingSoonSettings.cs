using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sweeprr.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLeavingSoonSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LeavingSoonCollectionId",
                table: "GlobalSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LeavingSoonSyncEnabled",
                table: "GlobalSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LeavingSoonCollectionId",
                table: "GlobalSettings");

            migrationBuilder.DropColumn(
                name: "LeavingSoonSyncEnabled",
                table: "GlobalSettings");
        }
    }
}
