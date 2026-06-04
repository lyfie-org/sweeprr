using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sweeprr.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Level = table.Column<int>(type: "INTEGER", nullable: false),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    MetaJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Exclusions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MediaServerItemId = table.Column<string>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Exclusions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GlobalSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InstanceName = table.Column<string>(type: "TEXT", nullable: false),
                    JwtSecret = table.Column<string>(type: "TEXT", nullable: false),
                    MaxItemsPerRun = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxGbPerRun = table.Column<double>(type: "REAL", nullable: false),
                    GlobalDryRun = table.Column<bool>(type: "INTEGER", nullable: false),
                    DefaultCron = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalSettings", x => x.Id);
                    table.CheckConstraint("CK_GlobalSettings_SingleRow", "Id = 1");
                });

            migrationBuilder.CreateTable(
                name: "RuleGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    MediaType = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CronOverride = table.Column<string>(type: "TEXT", nullable: true),
                    Action = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServerConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", nullable: false),
                    ApiKeyEncrypted = table.Column<string>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastConnectedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastConnectionOk = table.Column<bool>(type: "INTEGER", nullable: true),
                    ExtraJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Rules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RuleGroupId = table.Column<int>(type: "INTEGER", nullable: false),
                    Section = table.Column<int>(type: "INTEGER", nullable: false),
                    LogicalOperator = table.Column<int>(type: "INTEGER", nullable: true),
                    Field = table.Column<string>(type: "TEXT", nullable: false),
                    Comparator = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    ValueType = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Rules_RuleGroups_RuleGroupId",
                        column: x => x.RuleGroupId,
                        principalTable: "RuleGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SweepItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RuleGroupId = table.Column<int>(type: "INTEGER", nullable: false),
                    MediaServerItemId = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    MediaType = table.Column<int>(type: "INTEGER", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    MatchedRuleSummary = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ArrInstanceId = table.Column<int>(type: "INTEGER", nullable: true),
                    TmdbId = table.Column<string>(type: "TEXT", nullable: true),
                    TvdbId = table.Column<string>(type: "TEXT", nullable: true),
                    ImdbId = table.Column<string>(type: "TEXT", nullable: true),
                    FlaggedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SweptAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SweepItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SweepItems_RuleGroups_RuleGroupId",
                        column: x => x.RuleGroupId,
                        principalTable: "RuleGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogs_Category",
                table: "ActivityLogs",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogs_Timestamp",
                table: "ActivityLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_Exclusions_MediaServerItemId",
                table: "Exclusions",
                column: "MediaServerItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Rules_RuleGroupId",
                table: "Rules",
                column: "RuleGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_SweepItems_GroupItem",
                table: "SweepItems",
                columns: new[] { "RuleGroupId", "MediaServerItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_SweepItems_Status",
                table: "SweepItems",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityLogs");

            migrationBuilder.DropTable(
                name: "Exclusions");

            migrationBuilder.DropTable(
                name: "GlobalSettings");

            migrationBuilder.DropTable(
                name: "Rules");

            migrationBuilder.DropTable(
                name: "ServerConnections");

            migrationBuilder.DropTable(
                name: "SweepItems");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "RuleGroups");
        }
    }
}
