using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chronos.Master.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ArchivedProjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "archived_projects",
                columns: table => new
                {
                    archive_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    project_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    agent_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    agent_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    archived_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    purge_after_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_archived_projects", x => x.archive_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_archived_projects_project_name",
                table: "archived_projects",
                column: "project_name");

            migrationBuilder.CreateIndex(
                name: "IX_archived_projects_purge_after_utc",
                table: "archived_projects",
                column: "purge_after_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "archived_projects");
        }
    }
}
