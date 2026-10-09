using System.Text.Json;
using Almuerzo.Web.Data;
using Almuerzo.Web.Models;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace Almuerzo.Web.Services;

public class ReportService(AppDbContext db, StoragePaths paths)
{
    public async Task<string> GenerateAsync(string date, string? department = null, string format = "xlsx")
    {
        if (format is not ("xlsx" or "pdf")) throw new BusinessException("Formato inválido.");
        var data = await ReportLayout.Load(db, date, department); Directory.CreateDirectory(paths.Reports);
        var file = Path.Combine(paths.Reports, $"almuerzo-{date}-{Guid.NewGuid():N}.{format}");
        if (format == "pdf") { ReportLayout.Pdf(data, file); return file; }
        using var book = new XLWorkbook(); var index = 0;
        foreach (var block in data.Sections)
        {
            var name = block.Name; foreach (var ch in new[] { '[', ']', ':', '*', '?', '/', '\\' }) name = name.Replace(ch, ' ');
            name = $"{++index} {name}"; var sheet = book.Worksheets.Add(name[..Math.Min(31, name.Length)]); var labels = ReportLayout.Headers(data.Categories); var width = labels.Length;
            sheet.Range(3, 1, 4, width).Merge().Value = block.Name; sheet.Range(3, 1, 4, width).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center; sheet.Range(3, 1, 4, width).Style.Font.FontSize = 16; sheet.Range(3, 1, 4, width).Style.Font.Bold = true;
            sheet.Range(5, 1, 5, width).Merge().Value = $"PEDIDO ALMUERZO : {BusinessRules.Date(date):dd/MM/yyyy}"; sheet.Range(5, 1, 5, width).Style.Font.Bold = true;
            for (var c = 0; c < width; c++) sheet.Cell(7, c + 1).Value = labels[c]; var row = 8;
            foreach (var person in block.People) { var values = ReportLayout.Values(person, row - 7, data.Categories); for (var c = 0; c < values.Length; c++) sheet.Cell(row, c + 1).Value = values[c]; row++; }
            var header = sheet.Range(7, 1, 7, width); header.Style.Fill.BackgroundColor = XLColor.FromHtml("#2563eb"); header.Style.Font.FontColor = XLColor.White; header.Style.Font.Bold = true;
            var table = sheet.Range(7, 1, Math.Max(7, row - 1), width); table.Style.Alignment.WrapText = true; table.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center; table.Style.Border.InsideBorder = XLBorderStyleValues.Thin; table.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            sheet.Column(1).Width = 6; sheet.Column(2).Width = 28; sheet.Column(3).Width = 12; sheet.Column(4).Width = 25; sheet.Columns(5, width - 1).Width = 22; sheet.Column(width).Width = 35;
            sheet.Row(7).Height = 30; if (row > 8) { sheet.Rows(8, row - 1).AdjustToContents(); foreach (var r in sheet.Rows(8, row - 1)) if (r.Height < 30) r.Height = 30; }
            sheet.SheetView.FreezeRows(7); sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape; sheet.PageSetup.FitToPages(1, 0); sheet.PageSetup.SetRowsToRepeatAtTop(1, 7);
        }
        book.SaveAs(file); return file;
    }
    public async Task<int> EnqueueCloseAsync(string date, string? testRecipient = null)
    {
        var file = await GenerateAsync(date); var pdf = await GenerateAsync(date, format: "pdf"); var cfg = await db.Settings.ToDictionaryAsync(x => x.Key, x => x.Value); var count = 0;
        var recipients = testRecipient == null ? (await db.Users.Where(x => x.Role == Roles.GeneralAdmin && x.Status == "ACTIVO" && x.Department!.Status == "ACTIVO").Select(x => x.Email).ToListAsync()).Concat(LunchService.Split(cfg.GetValueOrDefault("ADMIN_EMAILS", ""))).Distinct().ToList() : [testRecipient];
        if (testRecipient == null && System.Text.Json.Nodes.JsonNode.Parse(cfg.GetValueOrDefault("RESPONSIBLES_EMAILS_JSON", "[]")) is System.Text.Json.Nodes.JsonArray copies)
            recipients.AddRange(copies.Select(x => x is System.Text.Json.Nodes.JsonObject ? LunchService.Str(x, "email") : x?.ToString() ?? "").Where(x => System.Net.Mail.MailAddress.TryCreate(x, out _)));
        void Queue(string recipient, string subject, string attachment, string extra) { db.Outbox.Add(new MailMessageRecord { Recipient = recipient, Subject = subject, Body = $"Adjuntos el Excel y PDF del pedido de almuerzo para el {date}.", AttachmentPath = attachment, ExtraAttachmentsJson = JsonSerializer.Serialize(new[] { extra }) }); count++; }
        foreach (var email in recipients.Distinct()) Queue(email, $"Reporte general de almuerzo {date}", file, pdf);
        if (testRecipient == null)
        {
            var admins = await db.Users.Where(x => x.Role == Roles.DepartmentAdmin && x.Status == "ACTIVO" && x.Department!.Status == "ACTIVO").ToListAsync();
            foreach (var group in admins.GroupBy(x => x.DepartmentId)) { if (group.Key == null) continue; var deptFile = await GenerateAsync(date, group.Key); var deptPdf = await GenerateAsync(date, group.Key, "pdf"); foreach (var admin in group) Queue(admin.Email, $"Reporte de almuerzo del departamento {date}", deptFile, deptPdf); }
        }
        return count;
    }
}
