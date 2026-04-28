using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Chronos.Master.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialMaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agents",
                columns: table => new
                {
                    agent_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    base_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    location = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    capabilities_json = table.Column<string>(type: "text", nullable: false),
                    registered_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_heartbeat_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cpu_percent = table.Column<double>(type: "double precision", nullable: true),
                    memory_percent = table.Column<double>(type: "double precision", nullable: true),
                    disk_percent = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agents", x => x.agent_id);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    utc_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    action = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    result = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    actor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    client_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    details = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "leader_lease",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    holder_instance_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    acquired_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lease_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leader_lease", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "project_placements",
                columns: table => new
                {
                    project_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    agent_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    agent_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    updated_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_placements", x => x.project_name);
                });

            migrationBuilder.CreateTable(
                name: "volume_placements",
                columns: table => new
                {
                    project_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    volume_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    agent_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    bytes_used = table.Column<long>(type: "bigint", nullable: true),
                    updated_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_volume_placements", x => new { x.project_name, x.volume_name, x.agent_id });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agents");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "leader_lease");

            migrationBuilder.DropTable(
                name: "project_placements");

            migrationBuilder.DropTable(
                name: "volume_placements");
        }
    }
}
