using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sweeprr.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExclusionCreatedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Exclusions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Exclusions");
        }
    }
}
