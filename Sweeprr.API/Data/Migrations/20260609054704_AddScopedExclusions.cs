using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sweeprr.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScopedExclusions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "Exclusions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RuleGroupId",
                table: "Exclusions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TagExclusions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TagName = table.Column<string>(type: "TEXT", nullable: false),
                    TagId = table.Column<int>(type: "INTEGER", nullable: false),
                    ServerConnectionId = table.Column<int>(type: "INTEGER", nullable: false),
                    RuleGroupId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagExclusions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TagExclusions_RuleGroups_RuleGroupId",
                        column: x => x.RuleGroupId,
                        principalTable: "RuleGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TagExclusions_ServerConnections_ServerConnectionId",
                        column: x => x.ServerConnectionId,
                        principalTable: "ServerConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Exclusions_RuleGroupId",
                table: "Exclusions",
                column: "RuleGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_TagExclusions_RuleGroupId",
                table: "TagExclusions",
                column: "RuleGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_TagExclusions_ServerConnectionId",
                table: "TagExclusions",
                column: "ServerConnectionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Exclusions_RuleGroups_RuleGroupId",
                table: "Exclusions",
                column: "RuleGroupId",
                principalTable: "RuleGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Exclusions_RuleGroups_RuleGroupId",
                table: "Exclusions");

            migrationBuilder.DropTable(
                name: "TagExclusions");

            migrationBuilder.DropIndex(
                name: "IX_Exclusions_RuleGroupId",
                table: "Exclusions");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "Exclusions");

            migrationBuilder.DropColumn(
                name: "RuleGroupId",
                table: "Exclusions");
        }
    }
}
