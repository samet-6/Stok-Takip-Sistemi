using Microsoft.EntityFrameworkCore;
using StokTakip.Infrastructure.Data;

namespace StokTakip.IntegrationTests;

/// <summary>
/// Stock movements are append-only, and since the rule moved into the database a trigger
/// refuses every UPDATE and DELETE — including the sweeps these tests use to take their own
/// rows back out. Cleaning up is maintenance, not application behaviour, so the guard is
/// lifted here deliberately and in exactly one place: three copies of this would be three
/// chances to leave it off.
/// </summary>
internal static class AppendOnlyGuard
{
    private const string Trigger = "TR_StockMovements_AppendOnly";

    /// <summary>
    /// Runs <paramref name="maintenance"/> with the append-only trigger disabled. The whole
    /// thing sits in one transaction, and PostgreSQL rolls a trigger's state back with it — so
    /// a failure half way through cannot leave the table unguarded for the tests that follow.
    /// </summary>
    public static async Task SuspendedAsync(
        AppDbContext context, Func<Task> maintenance, CancellationToken ct)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(ct);

        await context.Database.ExecuteSqlRawAsync(
            $"""ALTER TABLE "StockMovements" DISABLE TRIGGER "{Trigger}";""", ct);

        await maintenance();

        await context.Database.ExecuteSqlRawAsync(
            $"""ALTER TABLE "StockMovements" ENABLE TRIGGER "{Trigger}";""", ct);

        await transaction.CommitAsync(ct);
    }
}
