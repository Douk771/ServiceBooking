using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Orders;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §396.5 — the next order number of a shop's business day, atomically:
/// one <c>INSERT … ON CONFLICT DO UPDATE … RETURNING</c> in the order's own transaction. The row lock of the counter
/// serializes only the number hand-out; a rollback of the transaction rolls the number back too (no gaps except a rollback
/// after the hand-out, which is acceptable). The unique index (CompanyId, BusinessDate, Number) on Orders is the safety net.
/// </summary>
public class OrderNumberAllocator(AppDbContext db)
{
    public async Task<int> NextAsync(Guid companyId, DateOnly businessDate, CancellationToken ct = default)
    {
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("OrderNumberAllocator must run inside the order's transaction.");

        // A raw command: SqlQuery<T> would wrap the statement in a sub-select, which Postgres refuses for INSERT … RETURNING.
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            INSERT INTO "OrderDailyCounters" ("CompanyId", "BusinessDate", "LastNumber") VALUES (@company, @date, 1)
            ON CONFLICT ("CompanyId", "BusinessDate") DO UPDATE SET "LastNumber" = "OrderDailyCounters"."LastNumber" + 1
            RETURNING "LastNumber"
            """;
        AddParameter(command, "company", companyId);
        AddParameter(command, "date", businessDate);
        var result = await command.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
