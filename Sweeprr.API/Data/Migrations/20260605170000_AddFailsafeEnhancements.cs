using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sweeprr.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFailsafeEnhancements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "PessimisticSizeGb",
                table: "GlobalSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 5.0);

            migrationBuilder.AddColumn<double>(
                name: "LibraryPercentCap",
                table: "GlobalSettings",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "OverBroadMatchPct",
                table: "GlobalSettings",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PessimisticSizeGb",
                table: "GlobalSettings");

            migrationBuilder.DropColumn(
                name: "LibraryPercentCap",
                table: "GlobalSettings");

            migrationBuilder.DropColumn(
                name: "OverBroadMatchPct",
                table: "GlobalSettings");
        }
    }
}
