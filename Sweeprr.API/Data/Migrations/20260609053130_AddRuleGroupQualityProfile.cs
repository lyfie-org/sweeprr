using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sweeprr.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRuleGroupQualityProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TargetQualityProfileId",
                table: "RuleGroups",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetQualityProfileName",
                table: "RuleGroups",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TargetQualityProfileId",
                table: "RuleGroups");

            migrationBuilder.DropColumn(
                name: "TargetQualityProfileName",
                table: "RuleGroups");
        }
    }
}
