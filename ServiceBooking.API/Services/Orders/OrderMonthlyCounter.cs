using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Orders;

/// <summary>The month counter of an account after an increment.</summary>
public sealed record MonthlyUsage(int Count, DateTime? Warned80AtUtc, DateTime? Warned100AtUtc);

/// <summary>
/// ARCHITECTURE_CYCLE24.md §459.4 — orders CREATED per account per calendar month, incremented with ONE upsert inside the creation
/// transaction. The row lock of the counter serializes the orders of one account for the short tail of their transactions, which is
/// what makes the monthly limit hard: at <c>limit − 1</c> two simultaneous requests get counts <c>limit</c> and <c>limit + 1</c>, and the
/// second one rolls back. Lock order everywhere: <c>shop-stock:{shop}</c> → this counter → the number counter → the settings row.
/// </summary>
public class OrderMonthlyCounter(AppDbContext db)
{
    public async Task<MonthlyUsage> IncrementAsync(Guid billingAccountId, DateOnly month, CancellationToken ct = default)
    {
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("OrderMonthlyCounter must run inside the order's transaction.");
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            INSERT INTO "OrderMonthlyUsages" ("BillingAccountId", "Month", "Count") VALUES (@account, @month, 1)
            ON CONFLICT ("BillingAccountId", "Month") DO UPDATE SET "Count" = "OrderMonthlyUsages"."Count" + 1
            RETURNING "Count", "Warned80AtUtc", "Warned100AtUtc"
            """;
        Add(command, "account", billingAccountId);
        Add(command, "month", month);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new MonthlyUsage(
            reader.GetInt32(0),
            reader.IsDBNull(1) ? null : reader.GetDateTime(1),
            reader.IsDBNull(2) ? null : reader.GetDateTime(2));
    }

    /// <summary>Marks the 80 % / 100 % warning as sent — only if it was not yet; returns whether THIS call set it (the sender of the push).</summary>
    public async Task<bool> MarkWarnedAsync(Guid billingAccountId, DateOnly month, bool reached, DateTime nowUtc, CancellationToken ct = default)
    {
        var affected = reached
            ? await db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "OrderMonthlyUsages" SET "Warned100AtUtc" = {nowUtc} WHERE "BillingAccountId" = {billingAccountId} AND "Month" = {month} AND "Warned100AtUtc" IS NULL""", ct)
            : await db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "OrderMonthlyUsages" SET "Warned80AtUtc" = {nowUtc} WHERE "BillingAccountId" = {billingAccountId} AND "Month" = {month} AND "Warned80AtUtc" IS NULL""", ct);
        return affected > 0;
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
