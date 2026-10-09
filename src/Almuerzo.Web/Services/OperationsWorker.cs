

using Almuerzo.Web.Data;
using Almuerzo.Web.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Almuerzo.Web.Services;

public class OperationsWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<OperationsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!config.GetValue<bool>("Operations:Enabled")) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try { using var scope = scopes.CreateScope(); await Tick(scope.ServiceProvider, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError("Falló un ciclo de operaciones: {Type}", ex.GetType().Name); }
        }
    }
    private async Task Tick(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>(); var rules = services.GetRequiredService<BusinessRules>(); var lunch = services.GetRequiredService<LunchService>(); var paths = services.GetRequiredService<StoragePaths>(); var cfg = await lunch.Config(); var holidays = await lunch.Holidays();
        if (config.GetValue<bool>("Operations:ScheduledJobsEnabled") && BusinessRules.BusinessDay(BusinessRules.Date(rules.Today), holidays))
        {
            var targetDate = rules.NextBusinessDay(holidays).ToString("yyyy-MM-dd");
            foreach (var kind in new[] { "reminder", "close" })
            {
                var time = cfg[kind == "close" ? "HORA_ENVIO" : "HORA_RECORDATORIO"];
                if (!TimeOnly.TryParseExact(time, "HH:mm", out var due) || TimeOnly.FromDateTime(rules.Now.DateTime) < due) continue;
                var key = $"{rules.Today}:{kind}"; if (await db.JobRuns.AnyAsync(x => x.Key == key, ct)) continue;
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                if (kind == "close")
                {
                    await services.GetRequiredService<ReportService>().EnqueueCloseAsync(targetDate);
                }
                else
                {
                    if (await db.Menu.AnyAsync(x => x.Date == targetDate && x.Enabled, ct))
                    {
                        var ordered = await db.Orders.Where(x => x.Date == targetDate && x.Status == "ACTIVO").Select(x => x.Email).ToListAsync(ct);
                        var users = await db.Users.Where(x => x.Status == "ACTIVO" && x.Department!.Status == "ACTIVO" && !ordered.Contains(x.Email)).ToListAsync(ct);
                        foreach (var u in users) if (LunchService.Str(LunchService.Obj(u.PreferencesJson), "reminders", "true") != "false") lunch.Queue(u.Email, "Recordatorio de almuerzo", $"Recuerda seleccionar tu almuerzo para el {targetDate} antes del cierre.");
                    }
                }
                db.JobRuns.Add(new JobRun { Key = key }); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            }
        }
        if (config.GetValue<bool>("Operations:ScheduledJobsEnabled") && await db.JobRuns.AnyAsync(x => x.Key == $"{rules.Today}:close", ct) && !await db.JobRuns.AnyAsync(x => x.Key == $"{rules.Today}:backup", ct)) {
            Directory.CreateDirectory(paths.Backups); using var backup = new SqliteConnection($"Data Source={Path.Combine(paths.Backups, $"almuerzo-{rules.Today}.db")}"); backup.Open();
            await db.Database.OpenConnectionAsync(ct); ((SqliteConnection)db.Database.GetDbConnection()).BackupDatabase(backup);
            db.JobRuns.Add(new JobRun { Key = $"{rules.Today}:backup" }); await db.SaveChangesAsync(ct);
        }
        await services.GetRequiredService<OutboxDelivery>().DeliverAsync(cfg, ct);
    }
}
