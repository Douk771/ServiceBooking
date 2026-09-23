using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <summary>
    /// Proход B, US-125 (ARCHITECTURE_CYCLE9.md §104.5). Two columns on
    /// <c>CompanyNotificationSettings</c>, both <c>defaultValue: 0</c> — <c>PriorityChannel</c> /
    /// <c>WhatsApp</c> — which is exactly today's single-transport behavior (П12: "поведение всех
    /// существующих компаний не меняется вообще"). No backfill: a missing
    /// <c>CompanyNotificationSettings</c> row already means "every default" by this entity's own
    /// convention, so a company with no row here needs nothing done to it either. Additive and fully
    /// reversible — not one of the cycle's breaking migrations.
    /// </summary>
    public partial class AddDeliveryMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DeliveryMode",
                table: "CompanyNotificationSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PriorityTransport",
                table: "CompanyNotificationSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveryMode",
                table: "CompanyNotificationSettings");

            migrationBuilder.DropColumn(
                name: "PriorityTransport",
                table: "CompanyNotificationSettings");
        }
    }
}
