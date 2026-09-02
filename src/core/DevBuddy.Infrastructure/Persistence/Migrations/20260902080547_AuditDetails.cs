using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevBuddy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "details",
                table: "audit_events",
                type: "jsonb",
                nullable: false,

                // Changed by hand from the empty string EF generates: a jsonb column rejects it,
                // and existing rows genuinely have no detail to record.
                defaultValue: "{}");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_workspace_id_project_id_action_occurred_at",
                table: "audit_events",
                columns: new[] { "workspace_id", "project_id", "action", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_audit_events_workspace_id_project_id_action_occurred_at",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "details",
                table: "audit_events");
        }
    }
}
