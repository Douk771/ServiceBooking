using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingEventJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookingEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorKind = table.Column<int>(type: "integer", nullable: false),
                    ActorUserId = table.Column<string>(type: "text", nullable: true),
                    ActorNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ActorRoleSnapshot = table.Column<int>(type: "integer", nullable: true),
                    PreviousDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PreviousStartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    NewDate = table.Column<DateOnly>(type: "date", nullable: true),
                    NewStartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingEvents_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BookingEvents_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingEvents_ActorUserId",
                table: "BookingEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingEvents_BookingId_OccurredAtUtc",
                table: "BookingEvents",
                columns: new[] { "BookingId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingEvents_CompanyId_OccurredAtUtc",
                table: "BookingEvents",
                columns: new[] { "CompanyId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingEvents");
        }
    }
}
