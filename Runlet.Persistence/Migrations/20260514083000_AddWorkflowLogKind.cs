using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Runlet.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(RunletDbContext))]
    [Migration("20260514083000_AddWorkflowLogKind")]
    public partial class AddWorkflowLogKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "workflow_log_entries",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "System");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "kind",
                table: "workflow_log_entries");
        }
    }
}
