using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chronos.Master.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VolumeBackupAdaptiveDiskAndState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "max_cooldown_minutes",
                table: "volume_backup_policies",
                type: "integer",
                nullable: false,
                defaultValue: 10_080);

            migrationBuilder.AddColumn<int>(
                name: "minimum_free_disk_mb",
                table: "volume_backup_policies",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "minutes_cooldown_per_gb",
                table: "volume_backup_policies",
                type: "integer",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.CreateTable(
                name: "volume_backup_state",
                columns: table => new
                {
                    project_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    volume_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    last_backup_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_approx_bytes = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_volume_backup_state", x => new { x.project_name, x.volume_name });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "volume_backup_state");

            migrationBuilder.DropColumn(
                name: "max_cooldown_minutes",
                table: "volume_backup_policies");

            migrationBuilder.DropColumn(
                name: "minimum_free_disk_mb",
                table: "volume_backup_policies");

            migrationBuilder.DropColumn(
                name: "minutes_cooldown_per_gb",
                table: "volume_backup_policies");
        }
    }
}
