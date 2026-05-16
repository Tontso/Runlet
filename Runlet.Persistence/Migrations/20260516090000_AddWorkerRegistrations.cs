using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Runlet.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(RunletDbContext))]
    [Migration("20260516090000_AddWorkerRegistrations")]
    public partial class AddWorkerRegistrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "worker_registrations",
                columns: table => new
                {
                    worker_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    machine_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    max_concurrent_runs = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_heartbeat_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    stopped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_worker_registrations", x => x.worker_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_worker_registrations_last_heartbeat_at",
                table: "worker_registrations",
                column: "last_heartbeat_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "worker_registrations");
        }
    }
}
