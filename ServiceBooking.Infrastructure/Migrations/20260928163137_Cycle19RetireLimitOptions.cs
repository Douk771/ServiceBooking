using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Cycle19RetireLimitOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ARCHITECTURE_CYCLE19.md §383.4. Идемпотентно; ничего не удаляет; строки PlanOptionRules,
            // AccountSubscriptionOptions и RequestedOptionsJson не трогает. Предусловие (нет незавершённых
            // покупок опций-лимитов) проверяет гейт выката §385, а не эта миграция: сорвать её на старте
            // приложения без доступа к машине (TD16-4) нельзя.
            migrationBuilder.Sql("""
                UPDATE "SubscriptionOptions"
                SET "IsActive" = false, "UpdatedAtUtc" = now()
                WHERE lower(btrim("CapabilityKey")) IN ('employees', 'companies')
                  AND "IsActive" = true;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Намеренный no-op (тот же довод, что у SeedBillingCatalog): прежнее значение IsActive не
            // сохранено, а опция-лимит, снова ставшая активной, ничего не даёт новому коду и лишь
            // показывается старому.
        }
    }
}
