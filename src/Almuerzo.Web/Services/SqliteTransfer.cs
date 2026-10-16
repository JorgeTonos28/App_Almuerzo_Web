using Almuerzo.Web.Data;
using Almuerzo.Web.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Almuerzo.Web.Services;

public static class SqliteTransfer
{
    public static async Task<object> Run(AppDbContext target, string sourcePath, CancellationToken ct = default)
    {
        if (!target.Database.IsSqlServer()) throw new BusinessException("La transferencia requiere un destino SQL Server.");
        if (!File.Exists(sourcePath)) throw new BusinessException("No existe la base SQLite de origen.");
        foreach (var entity in target.Model.GetEntityTypes())
        {
            var table = entity.GetTableName()!;
            await using var command = target.Database.GetDbConnection().CreateCommand();
            await target.Database.OpenConnectionAsync(ct);
            command.CommandText = $"SELECT COUNT_BIG(*) FROM [{table}]";
            if (Convert.ToInt64(await command.ExecuteScalarAsync(ct)) != 0) throw new BusinessException("El destino SQL Server debe estar vacío. No se mezclan bases operativas.");
        }
        var temp = Path.Combine(Path.GetTempPath(), "almuerzo-transfer-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (var sourceConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(sourcePath), Mode = SqliteOpenMode.ReadOnly }.ToString()))
            using (var backup = new SqliteConnection("Data Source=" + temp)) { await sourceConnection.OpenAsync(ct); await backup.OpenAsync(ct); sourceConnection.BackupDatabase(backup); }
            await using var source = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=" + temp).Options);
            await source.Database.MigrateAsync(ct);
            await SeedData.InitializeAsync(source);
            target.AllowCrossRegionWrites = true;
            await using var tx = await target.Database.BeginTransactionAsync(ct);
            var counts = new Dictionary<string, int>();
            async Task Copy<T>(bool identity = false) where T : class
            {
                var rows = await source.Set<T>().IgnoreQueryFilters().AsNoTracking().ToListAsync(ct);
                var table = target.Model.FindEntityType(typeof(T))!.GetTableName()!;
                // Table names come exclusively from EF metadata, never request input.
                var identityOn = "SET IDENTITY_INSERT [" + table.Replace("]", "]]") + "] ON";
                var identityOff = "SET IDENTITY_INSERT [" + table.Replace("]", "]]") + "] OFF";
                if (identity && rows.Count > 0) await target.Database.ExecuteSqlRawAsync(identityOn, ct);
                target.Set<T>().AddRange(rows); await target.SaveChangesAsync(ct);
                if (identity && rows.Count > 0) await target.Database.ExecuteSqlRawAsync(identityOff, ct);
                if (await target.Set<T>().IgnoreQueryFilters().CountAsync(ct) != rows.Count) throw new BusinessException("La reconciliación de transferencia falló.");
                counts[table] = rows.Count; target.ChangeTracker.Clear();
            }
            await Copy<Region>(); await Copy<Department>(); await Copy<MenuCategory>(); await Copy<AppUser>(); await Copy<MenuItem>();
            await Copy<Order>(); await Copy<Holiday>(); await Copy<Setting>(); await Copy<Rating>(); await Copy<RatingHistory>(true);
            await Copy<AuditEvent>(true); await Copy<MailMessageRecord>(true); await Copy<ImportRecord>(); await Copy<SourceRecord>(true); await Copy<JobRun>(); await Copy<BrandingAsset>();
            await tx.CommitAsync(ct);
            return new { transferred = true, counts };
        }
        finally { target.AllowCrossRegionWrites = false; SqliteConnection.ClearAllPools(); File.Delete(temp); }
    }
}
