using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sweeprr.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBackupSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BackupSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    DestinationType = table.Column<int>(type: "INTEGER", nullable: false),
                    LocalPath = table.Column<string>(type: "TEXT", nullable: true),
                    S3Endpoint = table.Column<string>(type: "TEXT", nullable: true),
                    S3Region = table.Column<string>(type: "TEXT", nullable: true),
                    S3Bucket = table.Column<string>(type: "TEXT", nullable: true),
                    S3AccessKey = table.Column<string>(type: "TEXT", nullable: true),
                    S3SecretKeyEncrypted = table.Column<string>(type: "TEXT", nullable: true),
                    RetentionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ScheduleCron = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupSettings", x => x.Id);
                    table.CheckConstraint("CK_BackupSetting_SingleRow", "Id = 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackupSettings");
        }
    }
}
