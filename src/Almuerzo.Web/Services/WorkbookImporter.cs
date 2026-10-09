using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Almuerzo.Web.Data;
using Almuerzo.Web.Models;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.EntityFrameworkCore;

namespace Almuerzo.Web.Services;

public class WorkbookImporter(AppDbContext db)
{
    private static readonly Dictionary<string, string[]> Schema = new() {
        ["Config"] = ["key", "value", "description"], ["Usuarios"] = ["email", "nombre", "departamento", "rol", "estado", "preferencias_json", "codigo"],
        ["Departamentos"] = ["id", "nombre", "admins", "estado", "preferencias_json"], ["CategoriasMenu"] = ["id", "nombre", "orden", "estado", "alias_importacion", "es_combinable", "combinable_con", "tipo_seleccion"],
        ["Menu"] = ["id", "fecha", "categoria", "plato", "descripcion", "habilitado"], ["Pedidos"] = ["id", "fecha_solicitud", "fecha_consumo", "email_usuario", "nombre_usuario", "departamento", "seleccion_resumen", "json_detalle", "estado", "timestamp_modificacion", "creado_por"],
        ["DiasLibres"] = ["fecha", "motivo"], ["ValoracionesComida"] = ["id", "pedido_id", "fecha_consumo", "email_usuario", "nombre_usuario", "departamento", "puntuacion", "comentario", "platos_resumen", "timestamp_creacion", "timestamp_actualizacion"],
        ["ValoracionesProveedor"] = ["id", "proveedor_periodo_id", "proveedor_nombre", "email_usuario", "nombre_usuario", "departamento", "puntuacion", "comentario", "version_voto", "timestamp_creacion", "timestamp_actualizacion"],
        ["HistoricoValoracionesProveedor"] = ["id", "proveedor_periodo_id", "proveedor_nombre", "email_usuario", "nombre_usuario", "departamento", "puntuacion", "comentario", "timestamp"]
    };
    private static bool Secret(string key) => new[] { "TOKEN", "PASSWORD", "SECRET", "CREDENTIAL" }.Any(s => key.Contains(s, StringComparison.OrdinalIgnoreCase));
    private record SourceRow(string Sheet, int Row, Dictionary<string, string> Values);
    private static List<SourceRow> Read(string path)
    {
        using var doc = SpreadsheetDocument.Open(path, false); var workbook = doc.WorkbookPart ?? throw new BusinessException("Libro inválido.");
        var strings = workbook.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().Select(x => x.InnerText).ToArray() ?? [];
        var result = new List<SourceRow>(); var found = new HashSet<string>();
        foreach (var sheet in (workbook.Workbook?.Sheets ?? throw new BusinessException("Libro sin hojas.")).Elements<Sheet>())
        {
            var name = sheet.Name?.Value ?? ""; if (!Schema.TryGetValue(name, out var expected)) continue; found.Add(name);
            var part = (WorksheetPart)workbook.GetPartById(sheet.Id?.Value ?? throw new BusinessException("Hoja sin ID.")); var rows = (part.Worksheet ?? throw new BusinessException("Hoja inválida.")).Descendants<Row>(); string[]? headers = null;
            foreach (var row in rows)
            {
                var cells = new SortedDictionary<int, string>();
                foreach (var cell in row.Elements<Cell>())
                {
                    var col = 0; foreach (var ch in (cell.CellReference?.Value ?? throw new BusinessException("Celda sin referencia.")).TakeWhile(char.IsLetter)) col = col * 26 + char.ToUpperInvariant(ch) - 'A' + 1;
                    var raw = cell.CellValue?.InnerText ?? cell.InlineString?.InnerText ?? "";
                    if (cell.DataType?.Value == CellValues.SharedString && int.TryParse(raw, out var idx)) raw = strings[idx];
                    if (cell.DataType?.Value == CellValues.Boolean) raw = raw == "1" ? "TRUE" : "FALSE";
                    cells[col - 1] = raw;
                }
                if (headers == null)
                {
                    headers = Enumerable.Range(0, cells.Count == 0 ? 0 : cells.Keys.Max() + 1).Select(i => cells.GetValueOrDefault(i, "").Trim()).ToArray();
                    if (!expected.All(headers.Contains)) throw new BusinessException($"Encabezados incompatibles en {name}."); continue;
                }
                if (cells.Values.All(string.IsNullOrWhiteSpace)) continue;
                var values = headers.Select((h, i) => (h, v: cells.GetValueOrDefault(i, ""))).Where(x => x.h.Length > 0).ToDictionary(x => x.h, x => x.v);
                if (name == "Config" && Secret(values["key"])) values["value"] = "[REDACTED]";
                result.Add(new SourceRow(name, (int)(row.RowIndex?.Value ?? 0), values));
            }
        }
        if (!Schema.Keys.All(found.Contains)) throw new BusinessException("Faltan hojas obligatorias del modelo de origen."); return result;
    }
    private static string Date(string value)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)) return DateTime.FromOADate(serial).ToString("yyyy-MM-dd");
        if (value.Length >= 10 && DateOnly.TryParseExact(value[..10], "yyyy-MM-dd", out var date)) return date.ToString("yyyy-MM-dd");
        throw new BusinessException("Fecha inválida en origen.");
    }
    private static string Timestamp(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)) return new DateTimeOffset(DateTime.SpecifyKind(DateTime.FromOADate(serial), DateTimeKind.Unspecified), TimeSpan.FromHours(-4)).ToString("O");
        return value;
    }
    private static int Number(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) && v == Math.Truncate(v) && v is >= int.MinValue and <= int.MaxValue ? (int)v : 0;
    private static string Json(string value) { if (string.IsNullOrWhiteSpace(value)) return "{}"; JsonNode.Parse(value); return value; }
    public async Task<object> ImportAsync(string path)
    {
        if (!File.Exists(path) || !path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) throw new BusinessException("Selecciona un archivo XLSX existente.");
        await using var stream = File.OpenRead(path); var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream));
        if (await db.Imports.AnyAsync(x => x.Hash == hash)) return new { alreadyImported = true };
        // Baseline import only. Reject other populated datasets instead of merging conflicting histories.
        if (await db.Users.AnyAsync() || await db.Orders.AnyAsync() || await db.Menu.AnyAsync() || await db.Imports.AnyAsync()) throw new BusinessException("La importación inicial requiere una base sin datos operativos. Usa una base nueva para otro origen.");
        var rows = Read(path); var counts = Schema.Keys.ToDictionary(sheet => sheet, sheet => rows.Count(x => x.Sheet == sheet)); var warnings = new List<string>();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Categories.ExecuteDeleteAsync();
        foreach (var row in rows) db.SourceRecords.Add(new SourceRecord { Sheet = row.Sheet, Row = row.Row, Json = JsonSerializer.Serialize(row.Values) });
        foreach (var r in rows.Where(x => x.Sheet == "Departamentos")) { var v = r.Values; db.Departments.Add(new Department { Id = v["id"], Name = v["nombre"], Admins = v["admins"], Status = v["estado"], PreferencesJson = Json(v["preferencias_json"]) }); }
        foreach (var r in rows.Where(x => x.Sheet == "CategoriasMenu")) { var v = r.Values; db.Categories.Add(new MenuCategory { Id = v["id"], Name = v["nombre"], SortOrder = Number(v["orden"]), Status = v["estado"], Aliases = v["alias_importacion"], Combinable = v["es_combinable"] is "SI" or "TRUE", CombinableWith = v["combinable_con"], SelectionType = v["tipo_seleccion"] }); }
        await db.SaveChangesAsync();
        var deptIds = (await db.Departments.Select(x => x.Id).ToListAsync()).ToHashSet();
        foreach (var r in rows.Where(x => x.Sheet == "Usuarios"))
        {
            var v = r.Values; var role = v["rol"] == "USUARIO" ? Roles.User : v["rol"]; var dept = v["departamento"]; var validDept = deptIds.Contains(dept);
            var code = v["codigo"]; if (double.TryParse(code, out var numeric) && numeric % 1 == 0) code = ((int)numeric).ToString("D4");
            var game = new JsonObject(); foreach (var pair in new Dictionary<string, string> { ["monthKey"] = "juego_mes", ["score"] = "juego_puntos_mes", ["hits"] = "juego_aciertos_mes", ["misses"] = "juego_fallos_mes", ["dayKey"] = "juego_tiempo_fecha", ["usedSeconds"] = "juego_segundos_hoy", ["streak"] = "juego_racha", ["bestStreak"] = "juego_racha_max", ["penaltySeconds"] = "juego_penalizacion_segundos" }) { var raw = v.GetValueOrDefault(pair.Value, ""); game[pair.Key] = pair.Key is "monthKey" or "dayKey" ? raw : JsonValue.Create(Number(raw)); }
            if (!validDept) warnings.Add($"Usuarios fila {r.Row}: departamento no reconocido; requiere asignación antes de acceder.");
            if (!Roles.All.Contains(role)) throw new BusinessException($"Usuarios fila {r.Row}: rol desconocido.");
            db.Users.Add(new AppUser { Email = v["email"].Trim().ToLowerInvariant(), Name = v["nombre"], DepartmentId = validDept ? dept : null, Role = role, Status = v["estado"], EmployeeCode = code.Length == 0 ? null : code, PreferencesJson = Json(v["preferencias_json"]), GameJson = game.ToJsonString() });
        }
        foreach (var r in rows.Where(x => x.Sheet == "Menu")) { var v = r.Values; db.Menu.Add(new MenuItem { Id = v["id"], Date = Date(v["fecha"]), CategoryId = v["categoria"], Dish = v["plato"], Description = v["descripcion"], Enabled = v["habilitado"] == "SI" }); }
        foreach (var r in rows.Where(x => x.Sheet == "Pedidos")) { var v = r.Values; db.Orders.Add(new Order { Id = v["id"], RequestedAt = Timestamp(v["fecha_solicitud"]), Date = Date(v["fecha_consumo"]), Email = v["email_usuario"].Trim().ToLowerInvariant(), Name = v["nombre_usuario"], DepartmentId = v["departamento"], Summary = v["seleccion_resumen"], DetailJson = Json(v["json_detalle"]), Status = v["estado"], ModifiedAt = Timestamp(v["timestamp_modificacion"]), CreatedBy = v["creado_por"] }); }
        foreach (var r in rows.Where(x => x.Sheet == "DiasLibres")) db.Holidays.Add(new Holiday { Date = Date(r.Values["fecha"]), Reason = r.Values["motivo"] });
        foreach (var r in rows.Where(x => x.Sheet is "ValoracionesComida" or "ValoracionesProveedor"))
        {
            var v = r.Values; var meal = r.Sheet == "ValoracionesComida";
            db.Ratings.Add(new Rating { Id = v["id"], Kind = meal ? "MEAL" : "PROVIDER", Scope = meal ? Date(v["fecha_consumo"]) : v["proveedor_periodo_id"], OrderId = v.GetValueOrDefault("pedido_id", ""), ProviderName = v.GetValueOrDefault("proveedor_nombre", ""), Email = v["email_usuario"].ToLowerInvariant(), Name = v["nombre_usuario"], Department = v["departamento"], Score = Number(v["puntuacion"]), Comment = v["comentario"], Dishes = v.GetValueOrDefault("platos_resumen", ""), Version = Math.Max(1, Number(v.GetValueOrDefault("version_voto", "1"))), CreatedAt = Timestamp(v["timestamp_creacion"]), UpdatedAt = Timestamp(v["timestamp_actualizacion"]) });
        }
        foreach (var r in rows.Where(x => x.Sheet == "HistoricoValoracionesProveedor")) { var v = r.Values; db.RatingHistory.Add(new RatingHistory { VoteId = v["id"], PeriodId = v["proveedor_periodo_id"], ProviderName = v["proveedor_nombre"], Email = v["email_usuario"].ToLowerInvariant(), Name = v["nombre_usuario"], Department = v["departamento"], Score = Number(v["puntuacion"]), Comment = v["comentario"], Timestamp = Timestamp(v["timestamp"]) }); }
        await SeedData.InitializeAsync(db);
        var settings = await db.Settings.ToDictionaryAsync(x => x.Key);
        foreach (var r in rows.Where(x => x.Sheet == "Config"))
        {
            var key = r.Values["key"]; if (Secret(key) || !settings.TryGetValue(key, out var setting)) continue;
            var value = r.Values["value"];
            if (key is "HORA_ENVIO" or "HORA_RECORDATORIO" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)) value = DateTime.FromOADate(serial).ToString("HH:mm");
            if (key is "ANNOUNCEMENT_EXPIRES_ON" or "PROVIDER_PERIOD_START" && value.Length > 0) value = Date(value);
            // Old Apps Script URLs and live email settings never become active during migration.
            if (key is "APP_URL" or "TEST_EMAIL_DEST") value = ""; if (key == "TEST_EMAIL_MODE") value = "TRUE";
            setting.Value = value; setting.Description = r.Values["description"];
        }
        var summary = new { counts, warnings, secretsRedacted = rows.Count(x => x.Sheet == "Config" && Secret(x.Values["key"])), totalNonEmptyRows = rows.Count };
        db.Imports.Add(new ImportRecord { Hash = hash, SummaryJson = JsonSerializer.Serialize(summary) });
        await db.SaveChangesAsync();
        var actual = new Dictionary<string, int> { ["Usuarios"] = await db.Users.CountAsync(), ["Departamentos"] = await db.Departments.CountAsync(), ["CategoriasMenu"] = await db.Categories.CountAsync(), ["Menu"] = await db.Menu.CountAsync(), ["Pedidos"] = await db.Orders.CountAsync(), ["DiasLibres"] = await db.Holidays.CountAsync(), ["ValoracionesComida"] = await db.Ratings.CountAsync(x => x.Kind == "MEAL"), ["ValoracionesProveedor"] = await db.Ratings.CountAsync(x => x.Kind == "PROVIDER"), ["HistoricoValoracionesProveedor"] = await db.RatingHistory.CountAsync() };
        if (actual.Any(x => counts[x.Key] != x.Value) || await db.SourceRecords.CountAsync() != rows.Count) throw new BusinessException("La reconciliación falló. No se confirmaron cambios.");
        await tx.CommitAsync(); return summary;
    }
}
