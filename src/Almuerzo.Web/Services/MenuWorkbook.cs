using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using Almuerzo.Web.Models;
using ClosedXML.Excel;
using ExcelDataReader;

namespace Almuerzo.Web.Services;

public record MenuImportDay(string Date, Dictionary<string, List<string>> Items);
public record MenuWorkbookPreview(string WeekStart, string? SourceWeek, string SheetName, List<MenuImportDay> Preview, List<string> Warnings, int ItemCount, bool CanSave);

// A workbook is only a source of cell values. It never supplies authorization, category IDs or executable code.
public static class MenuWorkbook
{
    public const int MaxBytes = 2_000_000;
    private const string Marker = "ALMUERZO_MENU_V1";
    private static readonly string[] Days = ["LUNES", "MARTES", "MIERCOLES", "JUEVES", "VIERNES"];
    private static readonly string[] ReferenceHeaders = ["ARROCES", "GRANOS", "CARNES", "VIVERES", "ENSALADAS", "LASANAS Y O PASTELONES", "CALDOS", "FRITURITAS", "VEGETARIANA", "ALMUERZO VEGANO", "COMIDA RAPIDA"];
    private static string Key(string text) => Regex.Replace(string.Concat(text.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).ToUpperInvariant(), "[^A-Z0-9]+", " ").Trim();
    private static string Text(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? "";
    private static bool Empty(string text) => text.Length == 0 || Key(text) is "N A" or "NA";
    private static DateOnly Monday(string text)
    {
        var date = BusinessRules.Date(text);
        LunchService.Require(date.Year is >= 2000 and <= 2100 && date.DayOfWeek == DayOfWeek.Monday, "Selecciona un lunes entre 2000 y 2100 como inicio de semana.");
        return date;
    }
    private sealed record Sheet(string Name, List<object?[]> Rows);
    private static string Cell(Sheet sheet, int row, int column) => row < sheet.Rows.Count && column < sheet.Rows[row].Length ? Text(sheet.Rows[row][column]) : "";

    private static List<Sheet> Read(byte[] bytes, string extension, CancellationToken ct)
    {
        LunchService.Require(bytes.Length is > 0 and <= MaxBytes, "El Excel debe pesar como máximo 2 MB.");
        var ole = bytes.AsSpan().StartsWith(new byte[] { 0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1 });
        var zip = bytes.AsSpan().StartsWith("PK\u0003\u0004"u8);
        LunchService.Require(extension == ".xls" ? ole : extension == ".xlsx" && zip, "Carga un archivo Excel .xls o .xlsx válido, sin cambiarle la extensión.");
        if (zip) {
            using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            LunchService.Require(archive.Entries.Count <= 300 && archive.Entries.Sum(e => e.Length) <= 16_000_000 && archive.Entries.All(e => e.Length <= 8_000_000), "El contenido del Excel supera el tamaño permitido.");
            LunchService.Require(!archive.Entries.Any(e => e.FullName.Contains("vbaProject", StringComparison.OrdinalIgnoreCase) || e.FullName.Contains("externalLinks/", StringComparison.OrdinalIgnoreCase) || e.FullName.Contains("embeddings/", StringComparison.OrdinalIgnoreCase)), "Usa un Excel sin macros, vínculos externos ni objetos incrustados.");
            // Bound actual decompression too: do not rely only on ZIP directory lengths.
            int total = 0; var buffer = new byte[8192]; var budget = Stopwatch.StartNew();
            foreach (var entry in archive.Entries) {
                using var content = entry.Open(); int size = 0, read;
                while ((read = content.Read(buffer)) > 0) {
                    ct.ThrowIfCancellationRequested(); size += read; total += read;
                    LunchService.Require(size <= 8_000_000 && total <= 16_000_000 && budget.Elapsed < TimeSpan.FromSeconds(5), "El contenido del Excel supera el tamaño o plazo permitido.");
                }
            }
        }
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var input = new MemoryStream(bytes);
        using var reader = ExcelReaderFactory.CreateReader(input, new ExcelReaderConfiguration { LeaveOpen = false });
        var result = new List<Sheet>(); var watch = Stopwatch.StartNew();
        do {
            ct.ThrowIfCancellationRequested();
            LunchService.Require(result.Count < 5 && reader.FieldCount <= 32 && reader.RowCount <= 1000, "El Excel admite hasta 5 hojas, 32 columnas y 1000 filas por hoja.");
            var rows = new List<object?[]>();
            while (reader.Read()) {
                ct.ThrowIfCancellationRequested();
                LunchService.Require(watch.Elapsed < TimeSpan.FromSeconds(5) && rows.Count < 1000, "El Excel es demasiado complejo. Usa la plantilla de Almuerzo.");
                var cells = new object?[reader.FieldCount];
                for (var c = 0; c < cells.Length; c++) {
                    var value = reader.GetValue(c);
                    LunchService.Require(value is not string s || s.Length <= 2000, $"Celda demasiado larga en {reader.Name}, fila {rows.Count + 1}.");
                    cells[c] = value;
                }
                rows.Add(cells);
            }
            result.Add(new(reader.Name, rows));
        } while (reader.NextResult());
        return result;
    }

    private static Dictionary<string, string> CategoryMap(IReadOnlyList<MenuCategory> categories)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var category in categories.Where(c => c.Status == "ACTIVO")) {
            var aliases = category.Aliases.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Append(category.Name).Append(category.Id);
            // Explicit equivalences used by the supplied weekly-menu layout; never merge distinct categories.
            var builtIn = Key(category.Id.Split(':').Last()) switch {
                "VEGETARIANA" => new[] { "Almuerzo Vegano" },
                "OPCION RAPIDA" => new[] { "Comida Rapida" },
                "CALDO" => new[] { "Caldos" }, _ => Array.Empty<string>()
            };
            foreach (var alias in aliases.Concat(builtIn)) {
                var key = Key(alias); if (key.Length == 0) continue;
                LunchService.Require(!map.TryGetValue(key, out var other) || other == category.Id, $"El alias de categoría «{alias}» es ambiguo. Solicita su revisión a ADMIN_GEN.");
                map[key] = category.Id;
            }
        }
        return map;
    }

    private static DateOnly? SourceWeek(Sheet sheet, int header)
    {
        var months = new[] { "ENERO", "FEBRERO", "MARZO", "ABRIL", "MAYO", "JUNIO", "JULIO", "AGOSTO", "SEPTIEMBRE", "OCTUBRE", "NOVIEMBRE", "DICIEMBRE" };
        foreach (var row in sheet.Rows.Take(header)) foreach (var cell in row) {
            var match = Regex.Match(Key(Text(cell)), @"\bDEL (\d{1,2}) AL (\d{1,2}) (?:DE )?([A-Z]+) (\d{4})\b");
            if (!match.Success) continue;
            int month = Array.IndexOf(months, match.Groups[3].Value) + 1;
            if (month == 0 || !DateOnly.TryParseExact($"{match.Groups[4].Value}-{month:00}-{int.Parse(match.Groups[1].Value):00}", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)) continue;
            if (start.Year is >= 2000 and <= 2100 && start.DayOfWeek == DayOfWeek.Monday && start.AddDays(4).Day == int.Parse(match.Groups[2].Value)) return start;
        }
        return null;
    }

    public static MenuWorkbookPreview Preview(byte[] bytes, string filename, string? requestedWeek, IReadOnlyList<MenuCategory> categories, string regionId, HashSet<string> holidays, string today, CancellationToken ct = default)
    {
        var sheets = Read(bytes, Path.GetExtension(filename).ToLowerInvariant(), ct);
        var metadata = sheets.SingleOrDefault(s => s.Name == "_Almuerzo");
        var standard = metadata != null && Cell(metadata, 0, 0) == Marker;
        if (metadata != null) LunchService.Require(standard && Cell(metadata, 1, 0) == regionId, "La plantilla pertenece a otra regional o no es una plantilla válida. Descarga una nueva desde esta regional.");
        var candidates = sheets.Where(s => s.Name != "_Almuerzo").SelectMany(s => s.Rows.Take(20).Select((row, i) => (sheet: s, row, i)))
            .Where(x => x.row.Length >= 5 && Enumerable.Range(0, 5).All(c => Key(Text(x.row[c])) == Days[c])).ToArray();
        LunchService.Require(candidates.Length == 1, "Debe existir una sola hoja de menú con Lunes, Martes, Miércoles, Jueves y Viernes en las primeras cinco columnas.");
        var candidate = candidates[0]; var sheet = candidate.sheet;
        LunchService.Require(!sheet.Rows.Skip(candidate.i).Any(row => row.Skip(5).Any(v => Text(v).Length > 0)), "El menú debe ocupar únicamente las cinco columnas de lunes a viernes.");
        var source = standard && sheet.Rows.Count > 2 && sheet.Rows[2].Length > 1 && sheet.Rows[2][1] is DateTime dt ? DateOnly.FromDateTime(dt) : SourceWeek(sheet, candidate.i);
        LunchService.Require(!standard || source != null, "La plantilla necesita una fecha de lunes válida en B3.");
        if (standard) _ = Monday(source!.Value.ToString("yyyy-MM-dd"));
        var week = Monday(!string.IsNullOrWhiteSpace(requestedWeek) ? requestedWeek : source?.ToString("yyyy-MM-dd") ?? throw new BusinessException("Indica el lunes de la semana que vas a cargar."));
        var map = CategoryMap(categories); var warnings = new List<string>(); var all = Enumerable.Range(0, 5).Select(_ => new Dictionary<string, List<string>>()).ToArray();
        if (source != null && source != week) warnings.Add($"El archivo indica la semana del {source:yyyy-MM-dd}; se aplicará a la semana seleccionada del {week:yyyy-MM-dd}.");
        if (source == null) warnings.Add($"El archivo no indica una semana reconocible. Se usará el lunes seleccionado: {week:yyyy-MM-dd}.");
        string Category(string value, int row, int col) => map.TryGetValue(Key(value), out var cat) ? cat : throw new BusinessException($"Categoría «{value}» no configurada o inactiva ({(char)('A' + col)}{row + 1}). ADMIN_GEN debe revisar el catálogo o sus alias.");
        void Dish(string value, string? cat, int row, int col) {
            if (Empty(value)) return;
            LunchService.Require(cat != null, $"El plato en {(char)('A' + col)}{row + 1} necesita una categoría.");
            LunchService.Require(value.Length <= 300, $"El plato en {(char)('A' + col)}{row + 1} supera 300 caracteres.");
            if (!all[col].TryGetValue(cat!, out var items)) all[col][cat!] = items = [];
            var dish = BusinessRules.Text(value);
            LunchService.Require(!items.Contains(dish, StringComparer.OrdinalIgnoreCase), $"Plato duplicado en {(char)('A' + col)}{row + 1}, dentro de la misma categoría.");
            items.Add(dish);
            LunchService.Require(all[col].Values.Sum(v => v.Count) <= 200, "El menú de un día no puede superar 200 platos.");
        }
        if (standard) {
            LunchService.Require(candidate.i == 4 && int.TryParse(Cell(metadata!, 2, 0), out _), "La estructura de la plantilla fue modificada. Descarga una nueva.");
            int blocks = int.Parse(Cell(metadata!, 2, 0)); LunchService.Require(blocks is >= 1 and <= 20, "Cantidad de bloques inválida.");
            for (int block = 0; block < blocks; block++) for (int col = 0; col < 5; col++) {
                int row = 6 + block * 5; string label = Cell(sheet, row, col); var cat = Empty(label) ? null : Category(label, row, col);
                for (int slot = 1; slot <= 3; slot++) Dish(Cell(sheet, row + slot, col), cat, row + slot, col);
                LunchService.Require(Empty(Cell(sheet, row + 4, col)), $"Usa las tres casillas de platos del bloque; la celda {(char)('A' + col)}{row + 5} debe quedar vacía.");
            }
            LunchService.Require(!sheet.Rows.Skip(6 + blocks * 5).Any(row => row.Any(v => Text(v).Length > 0)), "Hay contenido fuera de los bloques de la plantilla.");
        } else {
            string?[] current = new string?[5];
            var headers = categories.SelectMany(c => c.Aliases.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Append(c.Name).Append(c.Id)).Select(Key).Concat(ReferenceHeaders).ToHashSet();
            for (int row = candidate.i + 1; row < sheet.Rows.Count; row++) for (int col = 0; col < 5; col++) {
                var value = Cell(sheet, row, col); if (Empty(value)) continue; var key = Key(value);
                if (map.ContainsKey(key) || headers.Contains(key)) current[col] = Category(value, row, col);
                else Dish(value, current[col], row, col);
            }
        }
        var preview = new List<MenuImportDay>();
        for (int col = 0; col < 5; col++) {
            var day = week.AddDays(col); if (all[col].Count == 0) continue;
            if (!BusinessRules.BusinessDay(day, holidays)) { warnings.Add($"Se omitió {day:yyyy-MM-dd}: feriado nacional o cierre regional."); continue; }
            preview.Add(new(day.ToString("yyyy-MM-dd"), all[col]));
        }
        LunchService.Require(preview.Count > 0, "No se encontraron platos para días habilitados de esa semana.");
        var savePayload = preview.ToDictionary(d => d.Date, d => d.Items.SelectMany(c => c.Value.Select(dish => new { cat = c.Key, plato = dish })).ToArray());
        // Reserve space for the RPC envelope, and conservatively account for escaped Unicode.
        LunchService.Require(JsonSerializer.SerializeToUtf8Bytes(savePayload).Length <= 240_000, "El menú supera el tamaño permitido para guardar. Divídelo en archivos más pequeños, dejando vacíos los otros días.");
        bool canSave = preview.All(d => string.CompareOrdinal(d.Date, today) > 0);
        if (!canSave) warnings.Add("La semana contiene fechas de hoy o pasadas. Vuelve y selecciona una semana futura antes de guardar.");
        return new(week.ToString("yyyy-MM-dd"), source?.ToString("yyyy-MM-dd"), sheet.Name, preview, warnings, preview.Sum(d => d.Items.Values.Sum(v => v.Count)), canSave);
    }

    public static byte[] Template(string weekStart, IReadOnlyList<MenuCategory> categories, string regionId, string regionName)
    {
        var week = Monday(weekStart); var active = categories.Where(c => c.Status == "ACTIVO").OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToArray();
        LunchService.Require(active.Length is >= 1 and <= 20, "La plantilla requiere entre 1 y 20 categorías activas.");
        _ = CategoryMap(active);
        using var book = new XLWorkbook(); var sheet = book.AddWorksheet("Menú semanal"); var meta = book.AddWorksheet("_Almuerzo");
        int blocks = Math.Max(10, active.Length);
        meta.Cell("A1").Value = Marker; meta.Cell("A2").Value = regionId; meta.Cell("A3").Value = blocks;
        for (int i = 0; i < active.Length; i++) meta.Cell(i + 6, 1).Value = active[i].Name;
        book.DefinedNames.Add("CategoriasAlmuerzo", meta.Range(6, 1, 5 + active.Length, 1));
        sheet.Style.Font.FontName = "Calibri"; sheet.Style.Font.FontSize = 11; sheet.Columns(1, 5).Width = 34;
        sheet.Cell("A1").Value = "Menú semanal"; sheet.Range("A1:E1").Merge().Style.Fill.BackgroundColor = XLColor.FromHtml("#EFF6FF"); sheet.Range("A1:E1").Style.Font.FontColor = XLColor.FromHtml("#17243A"); sheet.Range("A1:E1").Style.Font.FontSize = 18; sheet.Range("A1:E1").Style.Font.Bold = true; sheet.Row(1).Height = 36;
        sheet.Cell("A2").Value = regionName; sheet.Range("A2:E2").Merge().Style.Font.FontColor = XLColor.FromHtml("#64748B"); sheet.Row(2).Height = 24;
        sheet.Cell("A3").Value = "Semana (lunes):"; sheet.Cell("B3").Value = week.ToDateTime(TimeOnly.MinValue); sheet.Cell("B3").Style.DateFormat.Format = "dd/mm/yyyy"; sheet.Cell("B3").Style.Fill.BackgroundColor = XLColor.FromHtml("#EFF6FF");
        var monday = sheet.Cell("B3").CreateDataValidation(); monday.Custom("AND(ISNUMBER(B3),WEEKDAY(B3,2)=1)"); monday.ErrorTitle = "Selecciona un lunes"; monday.ErrorMessage = "La semana debe comenzar un lunes."; monday.ShowErrorMessage = true;
        sheet.Cell("C3").Value = "Una categoría y hasta tres platos por bloque."; sheet.Range("C3:E3").Merge(); sheet.Row(3).Height = 25;
        sheet.Cell("A4").Value = "Elige las categorías azules. Un plato por casilla. Para más platos, repite la categoría en otro bloque. Deja en blanco lo que no ofreces."; sheet.Range("A4:E4").Merge().Style.Alignment.WrapText = true; sheet.Range("A4:E4").Style.Font.FontSize = 10; sheet.Row(4).Height = 32;
        for (int col = 1; col <= 5; col++) { sheet.Cell(5, col).Value = Days[col - 1]; sheet.Cell(6, col).FormulaA1 = $"$B$3+{col - 1}"; sheet.Cell(6, col).Style.DateFormat.Format = "dd/mm/yyyy"; }
        sheet.Range("A5:E5").Style.Fill.BackgroundColor = XLColor.FromHtml("#DBEAFE"); sheet.Range("A5:E5").Style.Font.FontColor = XLColor.FromHtml("#1D4ED8"); sheet.Range("A5:E6").Style.Font.Bold = true; sheet.Range("A5:E6").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center; sheet.Row(5).Height = 25; sheet.Row(6).Height = 23;
        for (int block = 0; block < blocks; block++) {
            int row = 7 + block * 5;
            for (int col = 1; col <= 5; col++) {
                var category = sheet.Cell(row, col); if (block < active.Length) category.Value = active[block].Name;
                var validation = category.CreateDataValidation(); validation.List("=CategoriasAlmuerzo", true); validation.IgnoreBlanks = true; validation.ShowErrorMessage = true; validation.ErrorTitle = "Categoría no disponible"; validation.ErrorMessage = "Selecciona una categoría de la lista de esta regional.";
                category.Style.Fill.BackgroundColor = XLColor.FromHtml("#EFF6FF"); category.Style.Font.FontColor = XLColor.FromHtml("#1D4ED8"); category.Style.Font.Bold = true;
            }
            sheet.Row(row).Height = 27;
            var dishes = sheet.Range(row + 1, 1, row + 3, 5); dishes.Style.Alignment.WrapText = true; dishes.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center; dishes.Style.Border.InsideBorder = XLBorderStyleValues.Hair; dishes.Style.Border.InsideBorderColor = XLColor.FromHtml("#E2E8F0");
            sheet.Rows(row + 1, row + 3).Height = 52; sheet.Row(row + 4).Height = 10;
        }
        sheet.ShowGridLines = false; sheet.SheetView.FreezeRows(6); sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape; sheet.PageSetup.FitToPages(1, 0); sheet.PageSetup.SetRowsToRepeatAtTop(1, 6);
        meta.Visibility = XLWorksheetVisibility.VeryHidden;
        using var output = new MemoryStream(); book.SaveAs(output); return output.ToArray();
    }
}
