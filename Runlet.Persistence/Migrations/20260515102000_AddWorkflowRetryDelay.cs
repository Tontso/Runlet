using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Runlet.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(RunletDbContext))]
    [Migration("20260515102000_AddWorkflowRetryDelay")]
    public partial class AddWorkflowRetryDelay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "retry_delay_seconds",
                table: "workflow_runs",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "retry_delay_seconds",
                table: "workflow_runs");
        }
    }
}
