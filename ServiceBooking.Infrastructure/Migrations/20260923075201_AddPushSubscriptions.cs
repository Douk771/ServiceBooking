using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPushSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "StaffPushEnabled",
                table: "CompanyNotificationSettings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "PushSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    P256dhCiphertext = table.Column<string>(type: "text", nullable: false),
                    AuthCiphertext = table.Column<string>(type: "text", nullable: false),
                    KeyId = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    DeviceLabel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSuccessAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PushSubscriptions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StaffPushNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Payload = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<int>(type: "integer", nullable: true),
                    ReasonDetail = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffPushNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffPushNotifications_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_StaffPushNotifications_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffPushNotifications_PushSubscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "PushSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscriptions_Endpoint",
                table: "PushSubscriptions",
                column: "Endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscriptions_UserId",
                table: "PushSubscriptions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffPushNotifications_BookingId",
                table: "StaffPushNotifications",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffPushNotifications_CompanyId",
                table: "StaffPushNotifications",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffPushNotifications_Dispatch",
                table: "StaffPushNotifications",
                columns: new[] { "ExpiresAtUtc", "CreatedAt" },
                filter: "\"Status\" = 0")
                .Annotation("Npgsql:IndexInclude", new[] { "UserId", "CompanyId", "SubscriptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffPushNotifications_IdempotencyKey",
                table: "StaffPushNotifications",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffPushNotifications_SubscriptionId",
                table: "StaffPushNotifications",
                column: "SubscriptionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StaffPushNotifications");

            migrationBuilder.DropTable(
                name: "PushSubscriptions");

            migrationBuilder.DropColumn(
                name: "StaffPushEnabled",
                table: "CompanyNotificationSettings");
        }
    }
}
