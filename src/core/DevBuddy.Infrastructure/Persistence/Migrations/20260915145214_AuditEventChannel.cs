using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevBuddy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditEventChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "channel",
                table: "audit_events",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "channel",
                table: "audit_events");
        }
    }
}
