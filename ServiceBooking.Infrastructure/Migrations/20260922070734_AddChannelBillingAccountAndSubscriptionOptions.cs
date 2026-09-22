using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelBillingAccountAndSubscriptionOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BillingAccountId",
                table: "NotificationChannels",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AccountSubscriptionOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BillingAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    OptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    PaidUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActivatedByUserId = table.Column<string>(type: "text", nullable: true),
                    RequestedQuantity = table.Column<int>(type: "integer", nullable: true),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RequestedByUserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountSubscriptionOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountSubscriptionOptions_BillingAccounts_BillingAccountId",
                        column: x => x.BillingAccountId,
                        principalTable: "BillingAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccountSubscriptionOptions_SubscriptionOptions_OptionId",
                        column: x => x.OptionId,
                        principalTable: "SubscriptionOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationChannels_BillingAccountId",
                table: "NotificationChannels",
                column: "BillingAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountSubscriptionOptions_BillingAccountId_OptionId",
                table: "AccountSubscriptionOptions",
                columns: new[] { "BillingAccountId", "OptionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountSubscriptionOptions_OptionId",
                table: "AccountSubscriptionOptions",
                column: "OptionId");

            migrationBuilder.AddForeignKey(
                name: "FK_NotificationChannels_BillingAccounts_BillingAccountId",
                table: "NotificationChannels",
                column: "BillingAccountId",
                principalTable: "BillingAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_NotificationChannels_BillingAccounts_BillingAccountId",
                table: "NotificationChannels");

            migrationBuilder.DropTable(
                name: "AccountSubscriptionOptions");

            migrationBuilder.DropIndex(
                name: "IX_NotificationChannels_BillingAccountId",
                table: "NotificationChannels");

            migrationBuilder.DropColumn(
                name: "BillingAccountId",
                table: "NotificationChannels");
        }
    }
}
