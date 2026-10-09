using System.Net;
using System.Text.Json;
using Almuerzo.Web.Data;
using Almuerzo.Web.Models;
using Almuerzo.Web.Services;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Almuerzo.Tests;

public class ImportAndReportTests
{
    private static readonly Dictionary<string, string[]> Headers = new() {
        ["Config"] = ["key", "value", "description"],
        ["Usuarios"] = ["email", "nombre", "departamento", "rol", "estado", "preferencias_json", "codigo"],
        ["Departamentos"] = ["id", "nombre", "admins", "estado", "preferencias_json"],
        ["CategoriasMenu"] = ["id", "nombre", "orden", "estado", "alias_importacion", "es_combinable", "combinable_con", "tipo_seleccion"],
        ["Menu"] = ["id", "fecha", "categoria", "plato", "descripcion", "habilitado"],
        ["Pedidos"] = ["id", "fecha_solicitud", "fecha_consumo", "email_usuario", "nombre_usuario", "departamento", "seleccion_resumen", "json_detalle", "estado", "timestamp_modificacion", "creado_por"],
        ["DiasLibres"] = ["fecha", "motivo"],
        ["ValoracionesComida"] = ["id", "pedido_id", "fecha_consumo", "email_usuario", "nombre_usuario", "departamento", "puntuacion", "comentario", "platos_resumen", "timestamp_creacion", "timestamp_actualizacion"],
        ["ValoracionesProveedor"] = ["id", "proveedor_periodo_id", "proveedor_nombre", "email_usuario", "nombre_usuario", "departamento", "puntuacion", "comentario", "version_voto", "timestamp_creacion", "timestamp_actualizacion"],
        ["HistoricoValoracionesProveedor"] = ["id", "proveedor_periodo_id", "proveedor_nombre", "email_usuario", "nombre_usuario", "departamento", "puntuacion", "comentario", "timestamp"]
    };
    private static string Workbook(string directory, bool brokenMenu = false)
    {
        Directory.CreateDirectory(directory); var path = Path.Combine(directory, "synthetic.xlsx"); using var book = new XLWorkbook();
        foreach (var (name, headers) in Headers) { var sheet = book.AddWorksheet(name); for (var c = 0; c < headers.Length; c++) sheet.Cell(1, c + 1).Value = headers[c]; }
        void Row(string sheet, int row, params string[] values) { for (var c = 0; c < values.Length; c++) book.Worksheet(sheet).Cell(row, c + 1).Value = values[c]; }
        Row("Config", 2, "MENU_DAY_ENDPOINT_TOKEN", "synthetic-sensitive-token", "Privado"); Row("Config", 3, "TEST_EMAIL_MODE", "FALSE", "Modo de prueba");
        Row("Departamentos", 2, "a", "Departamento A", "", "ACTIVO", "{}");
        Row("Usuarios", 2, "sample@infotep.gob.do", "Colaborador sintético", "unknown", "USUARIO", "ACTIVO", "{\"reminders\":false,\"custom\":42}", "7");
        Row("CategoriasMenu", 2, "Arroces", "Arroces", "10", "ACTIVO", "Arroz", "SI", "", "UNICA");
        Row("Menu", 2, "menu-id", "46307", brokenMenu ? "missing" : "Arroces", "Arroz Blanco", "", "SI");
        Row("Pedidos", 2, "historic", "46300", "46307", "sample@infotep.gob.do", "Colaborador sintético", "a", "Arroz Blanco", "{\"items\":[\"Arroz Blanco\"],\"categorias\":[\"Arroces\"]}", "ACTIVO", "46300", "sample@infotep.gob.do");
        book.SaveAs(path); return path;
    }
    [Fact] public async Task ImportPreservesPrivateHistoryRedactsSecretsAndIsIdempotent()
    {
        await using var app = new AppFactory(); var path = Workbook(app.Storage);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var importer = scope.ServiceProvider.GetRequiredService<WorkbookImporter>();
        var summary = JsonSerializer.Serialize(await importer.ImportAsync(path)); Assert.Contains("secretsRedacted", summary); Assert.Contains("departamento no reconocido", summary);
        var user = await db.Users.FindAsync("sample@infotep.gob.do"); Assert.Null(user!.DepartmentId); Assert.Equal("0007", user.EmployeeCode); Assert.Equal(Roles.User, user.Role); Assert.Contains("\"custom\":42", user.PreferencesJson);
        Assert.Equal(1, await db.Orders.CountAsync()); Assert.Equal(0, await db.Ratings.CountAsync());
        Assert.DoesNotContain("synthetic-sensitive-token", string.Join("", await db.SourceRecords.Select(x => x.Json).ToListAsync()));
        Assert.Equal("TRUE", (await db.Settings.FindAsync("TEST_EMAIL_MODE"))!.Value); Assert.Null(await db.Settings.FindAsync("MENU_DAY_ENDPOINT_TOKEN"));
        Assert.Equal("{\"alreadyImported\":true}", JsonSerializer.Serialize(await importer.ImportAsync(path))); Assert.Equal(1, await db.Imports.CountAsync());
    }
    [Fact] public async Task InvalidForeignKeyRollsBackEntireImport()
    {
        await using var app = new AppFactory(); var path = Workbook(app.Storage, brokenMenu: true);
        using (var scope = app.Services.CreateScope()) await Assert.ThrowsAsync<DbUpdateException>(() => scope.ServiceProvider.GetRequiredService<WorkbookImporter>().ImportAsync(path));
        using var check = app.Services.CreateScope(); var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.Users.CountAsync()); Assert.Equal(0, await db.SourceRecords.CountAsync()); Assert.Equal(0, await db.Imports.CountAsync()); Assert.True(await db.Categories.AnyAsync());
    }
    [Fact] public async Task ReportScopeFiltersForeignAndCancelledOrdersInExcelAndPdf()
    {
        await using var app = new AppFactory(); await app.Seed();
        using (var scope = app.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var (email, name, dept, status) in new[] { ("user@infotep.gob.do", "Usuario A", "a", "ACTIVO"), ("other@infotep.gob.do", "Usuario B", "b", "ACTIVO"), ("old@infotep.gob.do", "Cancelado", "a", "CANCELADO") })
                db.Orders.Add(new Order { Email = email, Name = name, DepartmentId = dept, Date = "2026-10-12", Status = status, DetailJson = "{\"categorias\":[\"Arroces\"],\"items\":[\"Arroz Blanco\"],\"comentarios\":\"=SYNTHETIC()\"}" });
            await db.SaveChangesAsync();
        }
        using var c = app.Client(); await app.Login(c, "admin@infotep.gob.do"); var response = await c.GetAsync("/reports/daily/2026-10-12"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var memory = new MemoryStream(await response.Content.ReadAsByteArrayAsync()); using var excel = new XLWorkbook(memory); var sheet = excel.Worksheet(1);
        Assert.Equal("Usuario A", sheet.Cell(8, 2).GetString()); Assert.Equal("CÓDIGO", sheet.Cell(7, 3).GetString()); Assert.Equal("Arroz Blanco", sheet.Cell(8, 5).GetString());
        Assert.DoesNotContain("Usuario B", string.Join("", sheet.CellsUsed().Select(x => x.GetString()))); Assert.DoesNotContain("Cancelado", string.Join("", sheet.CellsUsed().Select(x => x.GetString())));
        Assert.False(sheet.Cell(8, sheet.LastColumnUsed()!.ColumnNumber()).HasFormula);
        var pdf = await c.GetAsync("/reports/daily/2026-10-12?format=pdf"); Assert.Equal(HttpStatusCode.OK, pdf.StatusCode); Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType); Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync())[..4]));
        using var user = app.Client(); await app.Login(user, "user@infotep.gob.do"); Assert.Equal(HttpStatusCode.Redirect, (await user.GetAsync("/reports/daily/2026-10-12")).StatusCode);
    }
}
