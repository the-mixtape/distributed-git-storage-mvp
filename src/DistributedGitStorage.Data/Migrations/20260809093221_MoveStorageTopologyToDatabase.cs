using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistributedGitStorage.Data.Migrations
{
    /// <inheritdoc />
    public partial class MoveStorageTopologyToDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "StorageClusterId",
                table: "repository_placements",
                type: "uuid",
                nullable: false);

            migrationBuilder.CreateTable(
                name: "storage_clusters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storage_clusters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "storage_nodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageClusterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    InternalAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storage_nodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_storage_nodes_storage_clusters_StorageClusterId",
                        column: x => x.StorageClusterId,
                        principalTable: "storage_clusters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_repository_placements_StorageClusterId",
                table: "repository_placements",
                column: "StorageClusterId");

            migrationBuilder.CreateIndex(
                name: "IX_storage_clusters_Name",
                table: "storage_clusters",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_storage_nodes_Name",
                table: "storage_nodes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_storage_nodes_StorageClusterId",
                table: "storage_nodes",
                column: "StorageClusterId");

            migrationBuilder.AddForeignKey(
                name: "FK_repository_placements_storage_clusters_StorageClusterId",
                table: "repository_placements",
                column: "StorageClusterId",
                principalTable: "storage_clusters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_repository_placements_storage_clusters_StorageClusterId",
                table: "repository_placements");

            migrationBuilder.DropTable(
                name: "storage_nodes");

            migrationBuilder.DropTable(
                name: "storage_clusters");

            migrationBuilder.DropIndex(
                name: "IX_repository_placements_StorageClusterId",
                table: "repository_placements");

            migrationBuilder.DropColumn(
                name: "StorageClusterId",
                table: "repository_placements");
        }
    }
}
