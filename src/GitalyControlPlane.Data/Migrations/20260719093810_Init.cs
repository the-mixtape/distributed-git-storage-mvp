using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GitalyControlPlane.Data.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "repository_placements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Storage = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StorageName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RelativePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_repository_placements", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "uq_repository_placements_name",
                table: "repository_placements",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "repository_placements");
        }
    }
}
