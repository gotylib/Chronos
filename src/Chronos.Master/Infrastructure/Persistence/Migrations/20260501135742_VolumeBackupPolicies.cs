using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chronos.Master.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VolumeBackupPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "volume_backup_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    volume_name_pattern = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    min_copies = table.Column<int>(type: "integer", nullable: false),
                    max_copies = table.Column<int>(type: "integer", nullable: false),
                    min_minutes_between_backups = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    extra_key_prefix = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    created_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_volume_backup_policies", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_volume_backup_policies_project_name",
                table: "volume_backup_policies",
                column: "project_name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "volume_backup_policies");
        }
    }
}
