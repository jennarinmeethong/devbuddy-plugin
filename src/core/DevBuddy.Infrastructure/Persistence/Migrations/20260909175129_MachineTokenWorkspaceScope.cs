using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevBuddy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MachineTokenWorkspaceScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_machine_tokens_user_id_expires_at",
                table: "machine_tokens");

            migrationBuilder.AddColumn<Guid>(
                name: "workspace_id",
                table: "machine_tokens",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_machine_tokens_user_id_workspace_id_expires_at",
                table: "machine_tokens",
                columns: new[] { "user_id", "workspace_id", "expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_machine_tokens_user_id_workspace_id_expires_at",
                table: "machine_tokens");

            migrationBuilder.DropColumn(
                name: "workspace_id",
                table: "machine_tokens");

            migrationBuilder.CreateIndex(
                name: "ix_machine_tokens_user_id_expires_at",
                table: "machine_tokens",
                columns: new[] { "user_id", "expires_at" });
        }
    }
}
