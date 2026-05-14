using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Runlet.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(RunletDbContext))]
    [Migration("20260514070000_AddWorkflowRunExecutionMode")]
    public partial class AddWorkflowRunExecutionMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "execution_mode",
                table: "workflow_runs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "LocalShell");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "execution_mode",
                table: "workflow_runs");
        }
    }
}
