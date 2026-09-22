using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookingServices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    NameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(10,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingServices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingServices_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BookingServices_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingServices_BookingId_Position",
                table: "BookingServices",
                columns: new[] { "BookingId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingServices_ServiceId",
                table: "BookingServices",
                column: "ServiceId");

            // ARCHITECTURE_CYCLE6.md §44.2 p.3: one BookingService row per existing Booking, Position 0,
            // so BookingDto.services is non-empty for every booking, old and new alike, and no screen
            // needs to branch on "was this booking created before this cycle". Not a data-preservation
            // measure — SPEC.md §0.1 explicitly allows deleting/recreating stage data this cycle — this
            // exists purely so downstream code has one shape to read, not two.
            migrationBuilder.Sql("""
                INSERT INTO "BookingServices" ("Id", "BookingId", "ServiceId", "Position", "NameSnapshot", "DurationMinutes", "Price")
                SELECT gen_random_uuid(), b."Id", b."ServiceId", 0,
                       COALESCE(s."Name", 'Услуга'),
                       GREATEST(1, CAST(ROUND(EXTRACT(EPOCH FROM (b."EndTime" - b."StartTime")) / 60) AS integer)),
                       b."Price"
                FROM "Bookings" b
                LEFT JOIN "Services" s ON s."Id" = b."ServiceId"
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingServices");
        }
    }
}
