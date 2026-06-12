using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sweeprr.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUpdatedAtToPlaybackActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No-op: the PlaybackActivities.UpdatedAt column is already created by the
            // 20260608114346_AddPlaybackActivity migration. This migration is kept as a
            // no-op to preserve the applied migration history on existing databases.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
