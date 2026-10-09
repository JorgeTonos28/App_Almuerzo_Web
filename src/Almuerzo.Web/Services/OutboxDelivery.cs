using System.Net;
using System.Net.Mail;
using Almuerzo.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Almuerzo.Web.Services;

public class OutboxDelivery(AppDbContext db, StoragePaths paths, MailDeliveryPolicy policy, IConfiguration config, ILogger<OutboxDelivery> logger)
{
    public async Task DeliverAsync(Dictionary<string, string> settings, CancellationToken ct)
    {
        var records = await db.Outbox.Where(x => x.SentAt == null && x.Attempts < 5).OrderBy(x => x.Id).Take(30).ToListAsync(ct);
        foreach (var record in records)
        {
            var route = policy.Resolve(settings, record.Recipient);
            if (route == null) { logger.LogWarning("Cola de correo retenida: revisa modo SMTP, host y destino único de prueba."); break; }
            try
            {
                using var message = new MailMessage(config["Mail:From"] ?? "almuerzo@infotep.gob.do", route.Destination, record.Subject, record.Body);
                message.From = new MailAddress(config["Mail:From"] ?? "almuerzo@infotep.gob.do", settings.GetValueOrDefault("MAIL_SENDER_NAME", "Comedor Institucional"));
                message.SubjectEncoding = message.BodyEncoding = System.Text.Encoding.UTF8;
                message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(record.Body, System.Text.Encoding.UTF8, "text/plain"));
                message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(MailTemplate.Html(record, settings), System.Text.Encoding.UTF8, "text/html"));
                var attachments = new[] { record.AttachmentPath }.Concat(System.Text.Json.JsonSerializer.Deserialize<string[]>(record.ExtraAttachmentsJson) ?? []);
                foreach (var attachment in attachments.Where(x => x.Length > 0)) { var full = Path.GetFullPath(attachment); if (!full.StartsWith(Path.GetFullPath(paths.Reports) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Ruta inválida."); message.Attachments.Add(new Attachment(full)); }
                using var smtp = new SmtpClient();
                if (route.Pickup) { Directory.CreateDirectory(paths.Mail); smtp.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory; smtp.PickupDirectoryLocation = paths.Mail; }
                else { smtp.Host = config["Mail:Host"]!; smtp.Port = config.GetValue<int>("Mail:Port", 587); smtp.EnableSsl = true; smtp.Credentials = new NetworkCredential(config["Mail:Username"], config["Mail:Password"]); }
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(30));
                await smtp.SendMailAsync(message, timeout.Token); record.SentAt = DateTimeOffset.UtcNow.ToString("O");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { record.Attempts++; logger.LogWarning("Correo {Id} pendiente tras intento {Attempts}: {Type}", record.Id, record.Attempts, ex.GetType().Name); }
        }
        await db.SaveChangesAsync(ct);
    }
}
