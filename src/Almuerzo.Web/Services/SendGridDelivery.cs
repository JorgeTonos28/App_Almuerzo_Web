using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Almuerzo.Web.Models;

namespace Almuerzo.Web.Services;

public class MailDeliveryException(int status, TimeSpan? retryAfter) : Exception("Proveedor de correo rechazó la solicitud.")
{
    public int Status { get; } = status;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

public class SendGridDelivery(HttpClient http, IConfiguration config)
{
    public async Task Send(MailMessageRecord record, string destination, Dictionary<string, string> settings, string[] paths, CancellationToken ct)
    {
        var apiKey = config["SendGrid:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("Falta la configuración privada de SendGrid.");
        // Fixed allowlisted destinations prevent redirecting institutional API keys to arbitrary hosts.
        var endpoint = config["SendGrid:DataResidency"] == "EU" ? "https://api.eu.sendgrid.com/v3/mail/send" : "https://api.sendgrid.com/v3/mail/send";
        var attachments = new List<object>(); long total = 0;
        foreach (var path in paths)
        {
            var length = new FileInfo(path).Length; total += length;
            if (length > 15_000_000 || total > 18_000_000) throw new IOException("Los adjuntos exceden el límite de envío.");
            attachments.Add(new { content = Convert.ToBase64String(await File.ReadAllBytesAsync(path, ct)), filename = Path.GetFileName(path), type = Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase) ? "application/pdf" : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", disposition = "attachment" });
        }
        var payload = new {
            personalizations = new[] { new { to = new[] { new { email = destination } } } },
            from = new { email = config["Mail:From"], name = settings.GetValueOrDefault("MAIL_SENDER_NAME", "Comedor Institucional") },
            subject = record.Subject,
            content = new[] { new { type = "text/plain", value = record.Body }, new { type = "text/html", value = MailTemplate.Html(record, settings) } },
            attachments,
            tracking_settings = new { click_tracking = new { enable = false, enable_text = false }, open_tracking = new { enable = false } }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = JsonContent.Create(payload) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode != HttpStatusCode.Accepted)
            throw new MailDeliveryException((int)response.StatusCode, response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow));
        // HTTP 202 means accepted by SendGrid, not proof of delivery to the recipient's inbox.
    }
}
