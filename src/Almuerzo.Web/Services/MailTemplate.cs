using System.Net;
using Almuerzo.Web.Models;

namespace Almuerzo.Web.Services;

public static class MailTemplate
{
    public static string Html(MailMessageRecord mail, Dictionary<string, string> config)
    {
        string Encode(string value) => WebUtility.HtmlEncode(value);
        var appName = Encode(config.GetValueOrDefault("APP_TITLE", "Solicitud Almuerzo"));
        var url = config.GetValueOrDefault("APP_URL", "");
        var cta = Uri.TryCreate(url, UriKind.Absolute, out var address) && address.Scheme == "https"
            ? $"<div style=\"text-align:center;margin:32px 0\"><a href=\"{Encode(url)}\" style=\"background:#2563eb;color:white;padding:14px 28px;border-radius:8px;text-decoration:none;font-weight:bold;display:inline-block\">Abrir Almuerzo</a></div>" : "";
        return $"""
            <!doctype html><html lang="es"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
            <body style="margin:0;padding:0;font-family:Helvetica,Arial,sans-serif;background:#f9fafb">
            <div style="width:100%;background:#f9fafb;padding:40px 20px;box-sizing:border-box">
            <div style="background:white;border-radius:16px;max-width:600px;margin:0 auto;padding:40px;border:1px solid #e5e7eb">
            <h1 style="text-align:center;color:#111827;font-size:24px;font-weight:800;margin:0 0 30px">{Encode(mail.Subject)}</h1>
            <div style="color:#4b5563;font-size:16px;line-height:1.6;white-space:pre-line">{Encode(mail.Body)}</div>{cta}
            <div style="text-align:center;margin-top:40px;font-size:12px;color:#9ca3af"><p style="font-weight:600">{appName}</p><p>Dirección de Innovación - INFOTEP</p></div>
            </div></div></body></html>
            """;
    }
}
