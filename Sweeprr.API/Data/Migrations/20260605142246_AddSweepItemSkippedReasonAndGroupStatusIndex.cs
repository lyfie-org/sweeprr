using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sweeprr.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSweepItemSkippedReasonAndGroupStatusIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SkippedReason",
                table: "SweepItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SweepItems_GroupStatus",
                table: "SweepItems",
                columns: new[] { "RuleGroupId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SweepItems_GroupStatus",
                table: "SweepItems");

            migrationBuilder.DropColumn(
                name: "SkippedReason",
                table: "SweepItems");
        }
    }
}
