using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistributedGitStorage.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReplicaTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CurrentGeneration",
                table: "repository_placements",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "replication_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceNode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TargetNode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Generation = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LockedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_replication_jobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_replication_jobs_repository_placements_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "repository_placements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "repository_replicas",
                columns: table => new
                {
                    RepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageNode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AppliedGeneration = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RefsHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastSuccessfulReplicationAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_repository_replicas", x => new { x.RepositoryId, x.StorageNode });
                    table.ForeignKey(
                        name: "FK_repository_replicas_repository_placements_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "repository_placements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_replication_jobs_RepositoryId_TargetNode_Generation",
                table: "replication_jobs",
                columns: new[] { "RepositoryId", "TargetNode", "Generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_replication_jobs_Status_NextAttemptAt",
                table: "replication_jobs",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_repository_replicas_RepositoryId_Status_AppliedGeneration",
                table: "repository_replicas",
                columns: new[] { "RepositoryId", "Status", "AppliedGeneration" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "replication_jobs");

            migrationBuilder.DropTable(
                name: "repository_replicas");

            migrationBuilder.DropColumn(
                name: "CurrentGeneration",
                table: "repository_placements");
        }
    }
}
