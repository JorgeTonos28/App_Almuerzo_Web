using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Almuerzo.Web.Data;
using Almuerzo.Web.Models;
using Almuerzo.Web.Services;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Almuerzo.Tests;

public class MenuWorkbookTests
{
    private static List<MenuCategory> Categories(string prefix = "") => [
        new() { Id = prefix + "Arroces", Name = "Arroces", SortOrder = 1 },
        new() { Id = prefix + "Carnes", Name = "Carnes", SortOrder = 2 },
        new() { Id = prefix + "Vegetariana", Name = "Vegetariana", SortOrder = 3 },
        new() { Id = prefix + "Caldo", Name = "Caldo", SortOrder = 4, Aliases = "Caldos" },
        new() { Id = prefix + "Opcion_Rapida", Name = "Opción rápida", SortOrder = 5 }
    ];
    // ClosedXML retains its last target stream when the same workbook is saved again.
    private static byte[] Bytes(XLWorkbook book) { var output = new MemoryStream(); book.SaveAs(output, new ClosedXML.Excel.SaveOptions { EvaluateFormulasBeforeSaving = true }); return output.ToArray(); }
    private static byte[] Template(string region = "central", List<MenuCategory>? categories = null) => MenuWorkbook.Template("2026-10-12", categories ?? Categories(), region, "Regional sintética");
    private static byte[] Filled(string region = "central", List<MenuCategory>? categories = null)
    {
        using var book = new XLWorkbook(new MemoryStream(Template(region, categories))); var sheet = book.Worksheet("Menú semanal");
        for (int col = 2; col <= 6; col++) sheet.Cell(6, col).Value = "ARROZ DE PRUEBA";
        sheet.Cell("B7").Value = "Arroz integral de prueba"; return Bytes(book);
    }
    private static MenuWorkbookPreview Read(byte[] bytes, string? week = null, List<MenuCategory>? categories = null, string region = "central", HashSet<string>? holidays = null, string today = "2026-10-09")
        => MenuWorkbook.Preview(bytes, "menu.xlsx", week, categories ?? Categories(), region, holidays ?? [], today);

    [Fact] public void GeneratedTemplateHasDropDownsTypedWeekDatesAndSharedWeeklyCategories()
    {
        var bytes = Filled(); using var book = new XLWorkbook(new MemoryStream(bytes)); var sheet = book.Worksheet("Menú semanal");
        Assert.Equal(new DateTime(2026, 10, 12), sheet.Cell("B3").GetDateTime());
        Assert.Equal("Menú Semanal INFOTEP", sheet.Cell("A1").GetString());
        Assert.True(sheet.Cell("A6").HasDataValidation); Assert.Contains("CategoriasAlmuerzo", sheet.Cell("A6").GetDataValidation().Value);
        Assert.Equal(XLWorksheetVisibility.VeryHidden, book.Worksheet("_Almuerzo").Visibility);
        sheet.Cell("C6").Value = ""; sheet.Cell("C9").Value = "Proteína de prueba";
        var preview = Read(Bytes(book)); Assert.Equal(6, preview.ItemCount); Assert.Equal(5, preview.Preview.Count); Assert.True(preview.CanSave);
        Assert.Equal(new[] { "Arroz de Prueba", "Arroz Integral de Prueba" }, preview.Preview[0].Items["Arroces"]);
        Assert.Equal("Proteína de Prueba", Assert.Single(preview.Preview[1].Items["Carnes"]));
    }
    [Fact] public void ProtectedWorkbookAllowsOnlyInputsAndCategoryFilteringWithoutSelectingLockedCells()
    {
        var bytes = Template(); using var book = new XLWorkbook(new MemoryStream(bytes)); var sheet = book.Worksheet("Menú semanal");
        Assert.True(book.IsProtected); Assert.True(book.IsPasswordProtected); Assert.True(sheet.IsPasswordProtected);
        Assert.Equal(XLSheetProtectionElements.SelectUnlockedCells | XLSheetProtectionElements.AutoFilter, sheet.Protection.AllowedElements);
        Assert.False(sheet.Cell("B3").Style.Protection.Locked); Assert.False(sheet.Cell("A6").Style.Protection.Locked);
        Assert.All(sheet.Range("B6:F8").Cells(), cell => Assert.False(cell.Style.Protection.Locked));
        Assert.True(sheet.Cell("A1").Style.Protection.Locked); Assert.True(sheet.Cell("B4").Style.Protection.Locked); Assert.True(sheet.Cell("A7").Style.Protection.Locked);
        Assert.True(sheet.Cell("B4").Style.Protection.Hidden); Assert.True(book.Worksheet("_Almuerzo").IsPasswordProtected);
        using var zip = new ZipArchive(new MemoryStream(bytes)); XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using var xml = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open(); var document = XDocument.Load(xml);
        var protection = document.Root!.Element(ns + "sheetProtection")!;
        Assert.True((bool)protection.Attribute("selectLockedCells")!); Assert.False((bool?)protection.Attribute("selectUnlockedCells") ?? false);
        Assert.False((bool?)protection.Attribute("autoFilter") ?? false);
        var filter = document.Root.Element(ns + "autoFilter")!; Assert.Equal("A5:A20", (string?)filter.Attribute("ref"));
        using var package = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        Assert.Empty(new OpenXmlValidator().Validate(package).Select(e => e.Description));
        Assert.Throws<ArgumentException>(() => sheet.Unprotect("incorrecta"));
        sheet.Unprotect(MenuWorkbook.TemplateProtectionPassword); book.Unprotect(MenuWorkbook.TemplateProtectionPassword); Assert.False(book.IsProtected); Assert.False(sheet.IsProtected);
    }
    [Fact] public void ChangingOnlyMondayRecalculatesEveryDayAndTheImportedWeek()
    {
        using var book = new XLWorkbook(new MemoryStream(Filled())); var sheet = book.Worksheet("Menú semanal");
        sheet.Cell("B3").Value = new DateTime(2026, 10, 26);
        book.RecalculateAllFormulas();
        for (int col = 2; col <= 6; col++) Assert.Equal(new DateTime(2026, 10, 26).AddDays(col - 2).ToOADate(), sheet.Cell(4, col).GetDouble());
        Assert.Equal("2026-10-26", Read(Bytes(book)).WeekStart);
        sheet.Cell("B3").Value = new DateTime(2026, 10, 27); Assert.Throws<BusinessException>(() => Read(Bytes(book)));
    }
    [Fact] public void NativeCategoryFilterRemovesWholeWeekAndRestoresExistingDishesWithoutMacros()
    {
        using var book = new XLWorkbook(new MemoryStream(Filled())); var sheet = book.Worksheet("Menú semanal");
        sheet.Cell("B9").Value = "Pollo de prueba";
        book.RecalculateAllFormulas(); sheet.AutoFilter.Column(1).AddFilter("Arroces");
        Assert.All(Enumerable.Range(9, 3), row => Assert.True(sheet.Row(row).IsHidden));
        var selected = Read(Bytes(book)); Assert.Equal(6, selected.ItemCount); Assert.DoesNotContain(selected.Preview, d => d.Items.ContainsKey("Carnes")); Assert.Contains(selected.Warnings, w => w.Contains("4 categorías ocultas"));
        foreach (var category in Categories().Skip(1)) sheet.AutoFilter.Column(1).AddFilter(category.Name);
        var restored = Read(Bytes(book)); Assert.Equal(7, restored.ItemCount); Assert.Equal("Pollo de Prueba", Assert.Single(restored.Preview[0].Items["Carnes"]));
        sheet.Row(7).Hide(); Assert.Contains("parcialmente oculta", Assert.Throws<BusinessException>(() => Read(Bytes(book))).Message);
        sheet.Row(7).Unhide(); sheet.Rows(6, 20).Hide(); Assert.Throws<BusinessException>(() => Read(Bytes(book)));
    }
    [Fact] public void PreviousV1TemplatesRemainReadableWithDifferentCategoriesPerDay()
    {
        using var book = new XLWorkbook(); var sheet = book.AddWorksheet("Menú semanal"); var meta = book.AddWorksheet("_Almuerzo");
        meta.Cell("A1").Value = "ALMUERZO_MENU_V1"; meta.Cell("A2").Value = "central"; meta.Cell("A3").Value = 1;
        sheet.Cell("B3").Value = new DateTime(2026, 10, 12); sheet.Cell("B3").Style.DateFormat.Format = "dd/mm/yyyy";
        for (int col = 1; col <= 5; col++) { sheet.Cell(5, col).Value = new[] { "Lunes", "Martes", "Miércoles", "Jueves", "Viernes" }[col - 1]; sheet.Cell(7, col).Value = col == 1 ? "Arroces" : "Carnes"; sheet.Cell(8, col).Value = "Plato de prueba"; }
        var preview = Read(Bytes(book)); Assert.Equal(5, preview.ItemCount); Assert.Contains("Arroces", preview.Preview[0].Items.Keys); Assert.Contains("Carnes", preview.Preview[1].Items.Keys);
    }
    [Fact] public void OriginalFiveColumnLayoutUsesAnIndependentCategoryForEachDayAndKeepsMultilineCellsTogether()
    {
        using var book = new XLWorkbook(); var sheet = book.AddWorksheet("Semana proveedor");
        sheet.Cell("D3").Value = "DEL 05 AL 09 OCTUBRE 2026";
        var days = new[] { "Lunes", "Martes", "Miércoles", "Jueves", "Viernes" };
        for (int c = 1; c <= 5; c++) { sheet.Cell(5, c).Value = days[c - 1]; sheet.Cell(6, c).Value = c == 1 ? "Vegetariana" : "Almuerzo Vegano"; sheet.Cell(7, c).Value = "PLATO\nDE PRUEBA"; sheet.Cell(8, c).Value = "N/A"; }
        sheet.Cell("A9").Value = "Caldos"; sheet.Cell("B9").Value = "Comida Rapida"; sheet.Cell("A10").Value = "Caldo de prueba"; sheet.Cell("B10").Value = "Opción de prueba";
        var preview = Read(Bytes(book), "2026-10-12"); Assert.Equal("2026-10-05", preview.SourceWeek); Assert.Contains(preview.Warnings, x => x.Contains("semana seleccionada"));
        Assert.Equal(7, preview.ItemCount); Assert.Equal("Plato de Prueba", Assert.Single(preview.Preview[0].Items["Vegetariana"]));
        Assert.Contains("Caldo", preview.Preview[0].Items.Keys); Assert.Contains("Opcion_Rapida", preview.Preview[1].Items.Keys);
    }
    [Fact] public void HolidaysAreExcludedPastWeeksCannotBeConfirmedAndExplicitRemappingIsVisible()
    {
        var holiday = Read(Filled(), holidays: ["2026-10-13"]); Assert.Equal(4, holiday.Preview.Count); Assert.Contains(holiday.Warnings, x => x.Contains("2026-10-13"));
        var past = Read(Filled(), today: "2026-10-21"); Assert.False(past.CanSave); Assert.Equal(5, past.Preview.Count);
        var shifted = Read(Filled(), "2026-10-26", today: "2026-10-21"); Assert.True(shifted.CanSave); Assert.Contains(shifted.Warnings, x => x.Contains("2026-10-12") && x.Contains("2026-10-26"));
        var national = Read(Filled(), "2026-11-09"); Assert.DoesNotContain(national.Preview, x => x.Date == "2026-11-09");
    }
    [Fact] public void CategoriesCannotBeMissingInactiveAmbiguousOrBorrowedFromAnotherRegional()
    {
        using var book = new XLWorkbook(new MemoryStream(Filled())); var sheet = book.Worksheet("Menú semanal");
        sheet.Cell("A6").Value = "Categoría ajena"; Assert.Contains("A6", Assert.Throws<BusinessException>(() => Read(Bytes(book))).Message);
        sheet.Cell("A6").Value = ""; Assert.Contains("A6", Assert.Throws<BusinessException>(() => Read(Bytes(book))).Message);
        Assert.Throws<BusinessException>(() => Read(Filled("east", Categories("east:"))));
        var inactive = Categories(); inactive[0].Status = "INACTIVO"; Assert.Throws<BusinessException>(() => Read(Filled(), categories: inactive));
        var ambiguous = Categories(); ambiguous[1].Aliases = "Arróces"; Assert.Throws<BusinessException>(() => Read(Filled(), categories: ambiguous));
        var legacy = new XLWorkbook(); var old = legacy.AddWorksheet("Proveedor");
        for (int c = 1; c <= 5; c++) old.Cell(5, c).Value = new[] { "Lunes", "Martes", "Miércoles", "Jueves", "Viernes" }[c - 1];
        old.Cell("A6").Value = "Lasañas y/o Pastelones"; old.Cell("A7").Value = "Plato de prueba";
        Assert.Contains("A6", Assert.Throws<BusinessException>(() => Read(Bytes(legacy), "2026-10-12")).Message); legacy.Dispose();
    }
    [Fact] public void DuplicateDishesUnexpectedCellsAndModifiedTemplateStructureAreRejected()
    {
        using var book = new XLWorkbook(new MemoryStream(Filled())); var sheet = book.Worksheet("Menú semanal");
        sheet.Cell("B7").Value = "arroz de prueba"; Assert.Contains("duplicado", Assert.Throws<BusinessException>(() => Read(Bytes(book))).Message);
        sheet.Cell("B7").Clear(); sheet.Cell("G6").Value = "Plato fuera del menú"; Assert.Throws<BusinessException>(() => Read(Bytes(book)));
        sheet.Cell("G6").Clear(); sheet.Cell("A80").Value = "Fuera de bloques"; Assert.Throws<BusinessException>(() => Read(Bytes(book)));
        sheet.Cell("A80").Clear(); book.Worksheet("_Almuerzo").Cell("A3").Value = "inválido"; Assert.Throws<BusinessException>(() => Read(Bytes(book)));
    }
    [Fact] public void InvalidFileFormatsCompressedPayloadsAndEmbeddedCodeAreRejectedBeforeReadingCells()
    {
        Assert.Throws<BusinessException>(() => Read(new byte[MenuWorkbook.MaxBytes + 1])); Assert.Throws<BusinessException>(() => Read("invalid workbook"u8.ToArray()));
        Assert.Throws<BusinessException>(() => MenuWorkbook.Preview(Filled(), "renamed.xls", null, Categories(), "central", [], "2026-10-09"));
        using var input = new MemoryStream();
        using (var zip = new ZipArchive(input, ZipArchiveMode.Create, true)) { using var entry = zip.CreateEntry("xl/sharedStrings.xml").Open(); entry.Write(new byte[16_000_001]); }
        Assert.Throws<BusinessException>(() => Read(input.ToArray()));
        using var macro = new MemoryStream(); macro.Write(Filled());
        using (var zip = new ZipArchive(macro, ZipArchiveMode.Update, true)) { using var entry = zip.CreateEntry("xl/vbaProject.bin").Open(); entry.WriteByte(1); }
        Assert.Throws<BusinessException>(() => Read(macro.ToArray()));
        Assert.Throws<BusinessException>(() => Read(Filled(), "2026-10-13"));
    }
    private static async Task<HttpResponseMessage> Upload(AppFactory app, HttpClient client, byte[] bytes, string? week = null, bool csrf = true)
    {
        var token = AppFactory.RpcToken(await client.GetStringAsync("/"));
        var form = new MultipartFormDataContent(); form.Add(new ByteArrayContent(bytes), "file", "menu.xlsx"); if (week != null) form.Add(new StringContent(week), "weekStart");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/menu/workbook/preview") { Content = form };
        if (csrf) request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }
    [Fact] public void AWorkbookThatWouldExceedTheSaveRequestLimitIsRejectedBeforeOfferingConfirmation()
    {
        using var book = new XLWorkbook(); var sheet = book.AddWorksheet("Proveedor");
        for (int c = 1; c <= 5; c++) {
            sheet.Cell(5,c).Value = new[] {"Lunes","Martes","Miércoles","Jueves","Viernes"}[c-1]; sheet.Cell(6,c).Value = "Arroces";
            for(int i=0;i<200;i++) sheet.Cell(7+i,c).Value = i.ToString("D3") + new string('a',297);
        }
        Assert.Contains("tamaño permitido para guardar",Assert.Throws<BusinessException>(()=>Read(Bytes(book),"2026-10-12")).Message);
    }
    [Fact] public async Task PreviewRequiresCurrentManagementRightsAndCsrfAndDoesNotModifyMenusOrSendMail()
    {
        await using var app = new AppFactory(); await app.Seed();
        using var admin = app.Client(); using var user = app.Client(); using var dept = app.Client();
        await app.Login(admin, "general@infotep.gob.do"); await app.Login(user, "user@infotep.gob.do"); await app.Login(dept, "admin@infotep.gob.do");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/menu/workbook/template?weekStart=2026-10-12")).StatusCode);
        foreach (var client in new[] { user, dept }) { Assert.False((await client.GetAsync("/menu/workbook/template")).IsSuccessStatusCode); Assert.False((await Upload(app, client, Filled())).IsSuccessStatusCode); }
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(app, admin, Filled(), csrf: false)).StatusCode);
        var response = await Upload(app, admin, Filled()); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<JsonObject>())!; Assert.True(result["ok"]!.GetValue<bool>());
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.Menu.CountAsync()); Assert.Equal(0, await db.Outbox.CountAsync());
        var save = new JsonObject(); foreach (var day in result["result"]!["preview"]!.AsArray()) {
            var items = new JsonArray(); foreach (var category in day!["items"]!.AsObject()) foreach (var dish in category.Value!.AsArray()) items.Add(new JsonObject { ["cat"] = category.Key, ["plato"] = dish!.ToString() });
            save[day["date"]!.ToString()] = items;
        }
        Assert.True((await app.Rpc(admin, "apiSaveWeeklyMenu", save))["ok"]!.GetValue<bool>());
        Assert.Equal(6, await db.Menu.CountAsync());
        var duplicate = new Dictionary<string,object> { ["2026-10-12"] = new[] { new {cat="Arroces",plato="Duplicado"}, new {cat="Arroces",plato="duplicado"} } };
        Assert.False((await app.Rpc(admin,"apiSaveWeeklyMenu",duplicate))["ok"]!.GetValue<bool>()); Assert.Equal(6, await db.Menu.CountAsync());
    }
    [Fact] public async Task RegionalAdminDownloadsOnlyTheirCatalogAndCannotImportAForeignTemplate()
    {
        await using var app = new AppFactory(); await app.Seed();
        using (var scope = app.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Users.SingleAsync(u => u.Email == "user@infotep.gob.do")).Role = Roles.RegionalAdmin; await db.SaveChangesAsync();
        }
        using var client = app.Client(); await app.Login(client, "user@infotep.gob.do");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/menu/workbook/template")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Upload(app, client, Filled())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(app, client, Filled("east", Categories("east:")))).StatusCode);
        Assert.False((await app.Rpc(client,"apiSaveWeeklyMenu",new Dictionary<string,object> { ["2026-10-12"] = new[] {new {cat="east:Arroces",plato="No autorizado"}} }))["ok"]!.GetValue<bool>());
    }
}
