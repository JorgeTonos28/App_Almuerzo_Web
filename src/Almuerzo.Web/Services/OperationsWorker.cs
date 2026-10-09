using System.Net;
using System.Net.Mail;
using Almuerzo.Web.Data;
using Almuerzo.Web.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Almuerzo.Web.Services;

public class OperationsWorker(IServiceScopeFactory scopes, IConfiguration config, IWebHostEnvironment env, ILogger<OperationsWorker> logger) : BackgroundService
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
        if (BusinessRules.BusinessDay(BusinessRules.Date(rules.Today), holidays))
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
        if (await db.JobRuns.AnyAsync(x => x.Key == $"{rules.Today}:close", ct) && !await db.JobRuns.AnyAsync(x => x.Key == $"{rules.Today}:backup", ct)) {
            Directory.CreateDirectory(paths.Backups); using var backup = new SqliteConnection($"Data Source={Path.Combine(paths.Backups, $"almuerzo-{rules.Today}.db")}"); backup.Open();
            await db.Database.OpenConnectionAsync(ct); ((SqliteConnection)db.Database.GetDbConnection()).BackupDatabase(backup);
            db.JobRuns.Add(new JobRun { Key = $"{rules.Today}:backup" }); await db.SaveChangesAsync(ct);
        }
        var mail = await db.Outbox.Where(x => x.SentAt == null && x.Attempts < 5).OrderBy(x => x.Id).Take(30).ToListAsync(ct);
        foreach (var record in mail)
        {
            try
            {
                var test = cfg.GetValueOrDefault("TEST_EMAIL_MODE", "TRUE") == "TRUE"; var destination = test ? cfg.GetValueOrDefault("TEST_EMAIL_DEST", "") : record.Recipient;
                var smtpHost = config["Mail:Host"]; var pickup = env.IsDevelopment() || config["Mail:Mode"] == "Pickup";
                if (!pickup && (string.IsNullOrWhiteSpace(smtpHost) || string.IsNullOrWhiteSpace(destination))) continue;
                using var message = new MailMessage(config["Mail:From"] ?? "almuerzo@infotep.gob.do", pickup && destination.Length == 0 ? record.Recipient : destination, record.Subject, record.Body);
                message.From = new MailAddress(config["Mail:From"] ?? "almuerzo@infotep.gob.do", cfg.GetValueOrDefault("MAIL_SENDER_NAME", "Comedor Institucional"));
                message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(record.Body, System.Text.Encoding.UTF8, "text/plain"));
                message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(MailTemplate.Html(record, cfg), System.Text.Encoding.UTF8, "text/html"));
                var attachments = new[] { record.AttachmentPath }.Concat(System.Text.Json.JsonSerializer.Deserialize<string[]>(record.ExtraAttachmentsJson) ?? []);
                foreach (var attachment in attachments.Where(x => x.Length > 0)) { var full = Path.GetFullPath(attachment); if (!full.StartsWith(Path.GetFullPath(paths.Reports) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Ruta inválida."); message.Attachments.Add(new Attachment(full)); }
                using var smtp = new SmtpClient();
                if (pickup) { Directory.CreateDirectory(paths.Mail); smtp.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory; smtp.PickupDirectoryLocation = paths.Mail; }
                else { smtp.Host = smtpHost!; smtp.Port = config.GetValue<int>("Mail:Port", 587); smtp.EnableSsl = true; smtp.Credentials = new NetworkCredential(config["Mail:Username"], config["Mail:Password"]); }
                await smtp.SendMailAsync(message, ct); record.SentAt = DateTimeOffset.UtcNow.ToString("O");
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { record.Attempts++; logger.LogWarning("Correo {Id} pendiente tras intento {Attempts}: {Type}", record.Id, record.Attempts, ex.GetType().Name); }
        }
        await db.SaveChangesAsync(ct);
    }
}
