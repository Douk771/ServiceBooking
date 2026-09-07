using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledTaskState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduledTaskStates",
                columns: table => new
                {
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastStartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastFinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSucceeded = table.Column<bool>(type: "boolean", nullable: false),
                    LastDurationMs = table.Column<int>(type: "integer", nullable: false),
                    LastSummary = table.Column<string>(type: "text", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledTaskStates", x => x.Name);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduledTaskStates");
        }
    }
}
