using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chronos.Agent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAgent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "volume_archives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VolumeName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ProjectName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    StoredRelativePath = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    BytesApprox = table.Column<long>(type: "bigint", nullable: true),
                    CompressMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_volume_archives", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_volume_archives_ProjectName_VolumeName",
                table: "volume_archives",
                columns: new[] { "ProjectName", "VolumeName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "volume_archives");
        }
    }
}
