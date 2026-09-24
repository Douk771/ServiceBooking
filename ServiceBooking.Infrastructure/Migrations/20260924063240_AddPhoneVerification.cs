using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPhoneVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PhoneVerificationSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StatusTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    CanonicalPhone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    FailureReason = table.Column<int>(type: "integer", nullable: true),
                    MismatchedPhoneMasked = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ExternalAccountKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LinkedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsumableUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhoneVerificationSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhoneVerificationSessions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VerifiedPhones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    ExternalAccountKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerifiedPhones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VerifiedPhones_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VerifiedPhones_PhoneVerificationSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "PhoneVerificationSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_GuestPhone",
                table: "Bookings",
                column: "GuestPhone",
                filter: "\"GuestPhone\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PhoneVerificationSessions_PayloadHash",
                table: "PhoneVerificationSessions",
                column: "PayloadHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhoneVerificationSessions_Status_ExpiresAtUtc",
                table: "PhoneVerificationSessions",
                columns: new[] { "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PhoneVerificationSessions_UserId_CreatedAtUtc",
                table: "PhoneVerificationSessions",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_VerifiedPhones_ExternalAccountKey",
                table: "VerifiedPhones",
                column: "ExternalAccountKey");

            migrationBuilder.CreateIndex(
                name: "IX_VerifiedPhones_Phone",
                table: "VerifiedPhones",
                column: "Phone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VerifiedPhones_SessionId",
                table: "VerifiedPhones",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_VerifiedPhones_UserId",
                table: "VerifiedPhones",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VerifiedPhones");

            migrationBuilder.DropTable(
                name: "PhoneVerificationSessions");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_GuestPhone",
                table: "Bookings");
        }
    }
}
