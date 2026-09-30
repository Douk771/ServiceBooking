using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Cycle25OrdersInsightsMax : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "StaffMaxEnabled",
                table: "ShopSettings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "ShopCustomerNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    UpdatedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopCustomerNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShopCustomerNotes_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffMaxLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ChatKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ChatIdCiphertext = table.Column<string>(type: "text", nullable: true),
                    KeyId = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LinkedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StoppedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSuccessAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffMaxLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffMaxLinks_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StaffMaxLinkSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffMaxLinkSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffMaxLinkSessions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StaffMaxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChatKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
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
                    table.PrimaryKey("PK_StaffMaxMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffMaxMessages_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffMaxMessages_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CompanyId_CustomerPhone",
                table: "Orders",
                columns: new[] { "CompanyId", "CustomerPhone" },
                filter: "\"CustomerPhone\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Report",
                table: "Orders",
                columns: new[] { "CompanyId", "PickupDate" })
                .Annotation("Npgsql:IndexInclude", new[] { "Status", "EstimatedTotal", "FinalTotal" });

            migrationBuilder.CreateIndex(
                name: "IX_ShopCustomerNotes_CompanyId_Phone",
                table: "ShopCustomerNotes",
                columns: new[] { "CompanyId", "Phone" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffMaxLinks_ChatKey",
                table: "StaffMaxLinks",
                column: "ChatKey");

            migrationBuilder.CreateIndex(
                name: "IX_StaffMaxLinks_UserId",
                table: "StaffMaxLinks",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffMaxLinkSessions_PayloadHash",
                table: "StaffMaxLinkSessions",
                column: "PayloadHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffMaxLinkSessions_UserId",
                table: "StaffMaxLinkSessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffMaxMessages_CompanyId",
                table: "StaffMaxMessages",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffMaxMessages_Dispatch",
                table: "StaffMaxMessages",
                columns: new[] { "ExpiresAtUtc", "CreatedAt" },
                filter: "\"Status\" = 0")
                .Annotation("Npgsql:IndexInclude", new[] { "CompanyId", "ChatKey" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffMaxMessages_IdempotencyKey",
                table: "StaffMaxMessages",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffMaxMessages_OrderId",
                table: "StaffMaxMessages",
                column: "OrderId");

            // Data (ARCHITECTURE_CYCLE25.md §497.1, §519 p.1): shops were hard-coded to ShowInPublicListing = false, which was
            // never the owner's choice; and the system free plan of the Orders line now allows the public listing (Q-25-7).
            migrationBuilder.Sql("UPDATE \"Companies\" SET \"ShowInPublicListing\" = TRUE WHERE \"Kind\" = 1;");
            migrationBuilder.Sql("UPDATE \"SubscriptionPlanConfigs\" SET \"AllowPublicListing\" = TRUE WHERE \"IsSystemFree\" AND \"Line\" = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data revert (§497.4). Notes and MAX links are lost with their tables.
            migrationBuilder.Sql("UPDATE \"SubscriptionPlanConfigs\" SET \"AllowPublicListing\" = FALSE WHERE \"IsSystemFree\" AND \"Line\" = 1;");
            migrationBuilder.Sql("UPDATE \"Companies\" SET \"ShowInPublicListing\" = FALSE WHERE \"Kind\" = 1;");

            migrationBuilder.DropTable(
                name: "ShopCustomerNotes");

            migrationBuilder.DropTable(
                name: "StaffMaxLinks");

            migrationBuilder.DropTable(
                name: "StaffMaxLinkSessions");

            migrationBuilder.DropTable(
                name: "StaffMaxMessages");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CompanyId_CustomerPhone",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_Report",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "StaffMaxEnabled",
                table: "ShopSettings");
        }
    }
}
