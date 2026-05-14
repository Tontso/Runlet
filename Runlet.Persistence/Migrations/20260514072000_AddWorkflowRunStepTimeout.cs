using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Runlet.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(RunletDbContext))]
    [Migration("20260514072000_AddWorkflowRunStepTimeout")]
    public partial class AddWorkflowRunStepTimeout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "step_timeout_seconds",
                table: "workflow_runs",
                type: "integer",
                nullable: false,
                defaultValue: 300);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "step_timeout_seconds",
                table: "workflow_runs");
        }
    }
}
