using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTrialPlanFlagAndAccountTrialState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSystemTrial",
                table: "SubscriptionPlanConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "TrialChannelFirstAuthorizedAtUtc",
                table: "BillingAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TrialDurationDays",
                table: "BillingAccounts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TrialEndsAtUtc",
                table: "BillingAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TrialExpiredHandledAtUtc",
                table: "BillingAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TrialGrantSource",
                table: "BillingAccounts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrialGrantedByUserId",
                table: "BillingAccounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TrialMailingClosureLoggedAtUtc",
                table: "BillingAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TrialMailingWindowDays",
                table: "BillingAccounts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TrialMailingWindowEndsAtUtc",
                table: "BillingAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TrialStartedAtUtc",
                table: "BillingAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TrialTermsAcknowledgedAtUtc",
                table: "BillingAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrialTermsVersion",
                table: "BillingAccounts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TrialWarnedAtThresholdDays",
                table: "BillingAccounts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrialWarningThresholdsDays",
                table: "BillingAccounts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MailingUntilUtc",
                table: "AccountSubscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlanConfigs_IsSystemTrial",
                table: "SubscriptionPlanConfigs",
                column: "IsSystemTrial",
                unique: true,
                filter: "\"IsSystemTrial\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_BillingAccounts_TrialExpiry",
                table: "BillingAccounts",
                column: "TrialEndsAtUtc",
                filter: "\"TrialEndsAtUtc\" IS NOT NULL AND \"TrialExpiredHandledAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SubscriptionPlanConfigs_IsSystemTrial",
                table: "SubscriptionPlanConfigs");

            migrationBuilder.DropIndex(
                name: "IX_BillingAccounts_TrialExpiry",
                table: "BillingAccounts");

            migrationBuilder.DropColumn(
                name: "IsSystemTrial",
                table: "SubscriptionPlanConfigs");

            migrationBuilder.DropColumn(
                name: "TrialChannelFirstAuthorizedAtUtc",
                table: "BillingAccounts");

            migrationBuilder.DropColumn(
                name: "TrialDurationDays",
                table: "BillingAccounts");

            migrationBuilder.DropColumn(
                name: "TrialEndsAtUtc",
                table: "BillingAccounts");

            migrationBuilder.DropColumn(
                name: "TrialExpiredHandledAtUtc",
                table: "BillingAccounts");

            migrationBuilder.DropColumn(
                name: "TrialGrantSource",
                table: "BillingAccounts");

            migrationBuilder.DropColumn(
                name: "TrialGrantedByUserId",
                table: "BillingAccounts");

            migrationBuilder.DropColumn(
                name: "TrialMailingClosureLoggedAtUtc",
                table: "BillingAccounts");

            migrationBuilder.DropColumn(
                name: "TrialMailingWindowDays",
                table: "BillingAccounts");

            migrationBuilder.DropColumn(
                name: "TrialMailingWindowEndsAtUtc",
                table: "BillingAccounts");

            migrationBuilder.DropColumn(
                name: "TrialStartedAtUtc",
                table: "BillingAccounts");

            migrationBuilder.DropColumn(
                name: "TrialTermsAcknowledgedAtUtc",
                table: "BillingAccounts");

            migrationBuilder.DropColumn(
                name: "TrialTermsVersion",
                table: "BillingAccounts");

            migrationBuilder.DropColumn(
                name: "TrialWarnedAtThresholdDays",
                table: "BillingAccounts");

            migrationBuilder.DropColumn(
                name: "TrialWarningThresholdsDays",
                table: "BillingAccounts");

            migrationBuilder.DropColumn(
                name: "MailingUntilUtc",
                table: "AccountSubscriptions");
        }
    }
}
