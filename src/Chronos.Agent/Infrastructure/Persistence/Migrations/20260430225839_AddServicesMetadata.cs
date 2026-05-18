using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Chronos.Agent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServicesMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "services",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ServiceName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    DockerComposeFile = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    DockerComposeFilePath = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    ImageNames = table.Column<string>(type: "text", nullable: false),
                    VolumeNames = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_services", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_services_ServiceName",
                table: "services",
                column: "ServiceName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "services");
        }
    }
}
