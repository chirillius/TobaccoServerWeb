using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace TobacoServer.Models.DbContext;

internal static class DatabaseSchemaInitializer
{
    public static void Initialize(Microsoft.EntityFrameworkCore.DbContext context, Action ensureConversionEvents)
    {
        // Legacy databases were created without migrations. Do not create migration
        // history as a substitute for their application schema.
        if (context.Database.GetMigrations().Any())
        {
            context.Database.Migrate();
        }

        var creator = context.GetService<IRelationalDatabaseCreator>();
        if (!creator.Exists())
        {
            creator.Create();
        }

        using var transaction = context.Database.BeginTransaction();
        context.Database.ExecuteSqlRaw("""
DECLARE @result INT;
EXEC @result = sys.sp_getapplock
    @Resource = N'TobacoServer.SchemaInitialization',
    @LockMode = N'Exclusive',
    @LockOwner = N'Transaction',
    @LockTimeout = 15000;
IF @result < 0
    THROW 51000, 'Could not acquire the TobaccoServer schema initialization lock.', 1;
""");

        var existingTables = ReadApplicationTables(context);
        if (existingTables.Count == 0)
        {
            // Also handles an earlier failed Migrate() leaving only EF history/locks.
            // EnsureCreated() would skip all application tables in that case.
            creator.CreateTables();
        }
        else
        {
            var missingTables = context.Model.GetRelationalModel().Tables
                .Select(table => $"{table.Schema ?? "dbo"}.{table.Name}")
                .Where(table => !table.Equals("dbo.ConversionRegisterEvents", StringComparison.OrdinalIgnoreCase))
                .Where(table => !existingTables.Contains(table))
                .OrderBy(table => table)
                .ToArray();
            if (missingTables.Length > 0)
            {
                throw new InvalidOperationException(
                    "The TobaccoServer database has an incomplete or unrelated schema. Missing tables: "
                    + string.Join(", ", missingTables)
                    + ". Check the Default connection string or restore the full database backup. "
                    + "Existing tables and data have not been changed.");
            }
        }

        // Referenced model tables now exist. Keep the legacy additive upgrade.
        ensureConversionEvents();
        transaction.Commit();
    }

    private static HashSet<string> ReadApplicationTables(Microsoft.EntityFrameworkCore.DbContext context)
    {
        using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? 30;
        command.CommandText = """
SELECT s.[name], t.[name]
FROM sys.tables AS t
INNER JOIN sys.schemas AS s ON s.[schema_id] = t.[schema_id]
WHERE t.[is_ms_shipped] = 0
  AND NOT (s.[name] = N'dbo' AND t.[name] IN (N'__EFMigrationsHistory', N'__EFMigrationsLock'));
""";
        using var reader = command.ExecuteReader();
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            tables.Add($"{reader.GetString(0)}.{reader.GetString(1)}");
        }
        return tables;
    }
}
