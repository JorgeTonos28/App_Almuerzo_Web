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
    private const string MarkerV1 = "ALMUERZO_MENU_V1";
    private const string MarkerV2 = "ALMUERZO_MENU_V2";
    // Shared editing guard, deliberately documented in README; never an authentication secret.
    public const string TemplateProtectionPassword = "MenuINFOTEP2026!";
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
    private sealed record Sheet(string Name, List<object?[]> Rows, HashSet<int> HiddenRows);
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
            var rows = new List<object?[]>(); var hiddenRows = new HashSet<int>();
            while (reader.Read()) {
                ct.ThrowIfCancellationRequested();
                LunchService.Require(watch.Elapsed < TimeSpan.FromSeconds(5) && rows.Count < 1000, "El Excel es demasiado complejo. Usa la plantilla de Almuerzo.");
                var cells = new object?[reader.FieldCount];
                for (var c = 0; c < cells.Length; c++) {
                    var value = reader.GetValue(c);
                    LunchService.Require(value is not string s || s.Length <= 2000, $"Celda demasiado larga en {reader.Name}, fila {rows.Count + 1}.");
                    cells[c] = value;
                }
                if (reader.RowHeight == 0) hiddenRows.Add(rows.Count);
                rows.Add(cells);
            }
            result.Add(new(reader.Name, rows, hiddenRows));
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
        var compact = metadata != null && Cell(metadata, 0, 0) == MarkerV2;
        var standard = metadata != null && (compact || Cell(metadata, 0, 0) == MarkerV1);
        if (metadata != null) LunchService.Require(standard && Cell(metadata, 1, 0) == regionId, "La plantilla pertenece a otra regional o no es una plantilla válida. Descarga una nueva desde esta regional.");
        int offset = compact ? 1 : 0;
        var candidates = sheets.Where(s => s.Name != "_Almuerzo").SelectMany(s => s.Rows.Take(20).Select((row, i) => (sheet: s, row, i)))
            .Where(x => x.row.Length >= 5 + offset && Enumerable.Range(0, 5).All(c => Key(Text(x.row[c + offset])) == Days[c])).ToArray();
        LunchService.Require(candidates.Length == 1, "Debe existir una sola hoja de menú con las cinco columnas de lunes a viernes. Usa la plantilla o el formato de cinco columnas del proveedor.");
        var candidate = candidates[0]; var sheet = candidate.sheet;
        LunchService.Require(!sheet.Rows.Skip(candidate.i).Any(row => row.Skip(5 + offset).Any(v => Text(v).Length > 0)), "Hay contenido fuera de las columnas del menú.");
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
            LunchService.Require(cat != null, $"El plato en {(char)('A' + col + offset)}{row + 1} necesita una categoría.");
            LunchService.Require(value.Length <= 300, $"El plato en {(char)('A' + col + offset)}{row + 1} supera 300 caracteres.");
            if (!all[col].TryGetValue(cat!, out var items)) all[col][cat!] = items = [];
            var dish = BusinessRules.Text(value);
            LunchService.Require(!items.Contains(dish, StringComparer.OrdinalIgnoreCase), $"Plato duplicado en {(char)('A' + col + offset)}{row + 1}, dentro de la misma categoría.");
            items.Add(dish);
            LunchService.Require(all[col].Values.Sum(v => v.Count) <= 200, "El menú de un día no puede superar 200 platos.");
        }
        if (compact) {
            LunchService.Require(candidate.i == 4 && Key(Cell(sheet, 4, 0)) == "CATEGORIA" && int.TryParse(Cell(metadata!, 2, 0), out _), "La estructura de la plantilla fue modificada. Descarga una nueva.");
            int blocks = int.Parse(Cell(metadata!, 2, 0)); LunchService.Require(blocks is >= 1 and <= 20, "Cantidad de categorías inválida.");
            int excluded = 0;
            for (int block = 0; block < blocks; block++) {
                int row = 5 + block * 3;
                int hidden = Enumerable.Range(row, 3).Count(sheet.HiddenRows.Contains);
                LunchService.Require(hidden is 0 or 3, "Hay una categoría parcialmente oculta. Quita los filtros y selecciona únicamente las categorías desde la flecha de Categoría.");
                var cat = Category(Cell(sheet, row, 0), row, 0);
                if (hidden == 3) { excluded++; continue; }
                for (int col = 0; col < 5; col++) for (int slot = 0; slot < 3; slot++) Dish(Cell(sheet, row + slot, col + 1), cat, row + slot, col);
            }
            LunchService.Require(!sheet.Rows.Skip(5 + blocks * 3).Any(row => row.Any(v => Text(v).Length > 0)), "Hay contenido fuera de las categorías de la plantilla.");
            if (excluded > 0) warnings.Add($"Se excluyeron {excluded} categorías ocultas mediante el filtro del Excel; sus platos no se importarán.");
        } else if (standard) {
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
        LunchService.Require(preview.Count > 0, "No se encontraron platos para días habilitados de esa semana. Revisa también las categorías visibles en el filtro del Excel.");
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
        int blocks = active.Length, lastRow = 5 + blocks * 3;
        meta.Cell("A1").Value = MarkerV2; meta.Cell("A2").Value = regionId; meta.Cell("A3").Value = blocks;
        for (int i = 0; i < active.Length; i++) meta.Cell(i + 6, 1).Value = active[i].Name;
        book.DefinedNames.Add("CategoriasAlmuerzo", meta.Range(6, 1, 5 + active.Length, 1));
        var navy = XLColor.FromHtml("#17243A"); var blue = XLColor.FromHtml("#1D4ED8"); var pale = XLColor.FromHtml("#EFF6FF"); var line = XLColor.FromHtml("#DBEAFE");
        sheet.Style.Font.FontName = "Calibri"; sheet.Style.Font.FontSize = 11; sheet.Style.Font.FontColor = navy;
        sheet.Style.Protection.Locked = true; sheet.Column(1).Width = 24; sheet.Columns(2, 6).Width = 29;
        sheet.Range(1, 1, lastRow, 6).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        sheet.Cell("A1").Value = "Menú Semanal INFOTEP";
        var title = sheet.Range("A1:F1").Merge(); title.Style.Fill.BackgroundColor = pale; title.Style.Font.FontSize = 23; title.Style.Font.Bold = true; title.Style.Alignment.Indent = 1; sheet.Row(1).Height = 46;
        sheet.Cell("A2").Value = regionName + " · Planificación de almuerzos";
        var subtitle = sheet.Range("A2:F2").Merge(); subtitle.Style.Font.FontSize = 11; subtitle.Style.Font.FontColor = XLColor.FromHtml("#64748B"); subtitle.Style.Alignment.Indent = 2; subtitle.Style.Border.BottomBorder = XLBorderStyleValues.Thin; subtitle.Style.Border.BottomBorderColor = line; sheet.Row(2).Height = 28;

        sheet.Cell("A3").Value = "Semana (lunes)"; sheet.Cell("A3").Style.Font.Bold = true; sheet.Cell("A3").Style.Alignment.Indent = 1;
        var date = sheet.Cell("B3"); date.Value = week.ToDateTime(TimeOnly.MinValue); date.Style.DateFormat.Format = "dd/mm/yyyy"; date.Style.Fill.BackgroundColor = line; date.Style.Font.FontColor = blue; date.Style.Font.FontSize = 14; date.Style.Font.Bold = true; date.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center; date.Style.Protection.Locked = false;
        var monday = date.CreateDataValidation(); monday.Custom("AND(ISNUMBER(B3),B3=INT(B3),YEAR(B3)>=2000,YEAR(B3)<=2100,WEEKDAY(B3,2)=1)"); monday.IgnoreBlanks = false; monday.ErrorStyle = XLErrorStyle.Stop; monday.ErrorTitle = "Selecciona un lunes"; monday.ErrorMessage = "Escribe una fecha de lunes entre 2000 y 2100."; monday.ShowErrorMessage = true; monday.InputTitle = "Inicio de la semana"; monday.InputMessage = "Cambia solo este lunes. Las cinco fechas se actualizan automáticamente."; monday.ShowInputMessage = true;
        sheet.Cell("C3").Value = "En la flecha de Categoría, marca las que ofrecerás. Para volver a agregarlas, márcalas otra vez.";
        var instructions = sheet.Range("C3:F3").Merge(); instructions.Style.Font.FontSize = 10; instructions.Style.Font.FontColor = XLColor.FromHtml("#64748B"); instructions.Style.Alignment.WrapText = true; instructions.Style.Alignment.Indent = 1; sheet.Row(3).Height = 42;

        sheet.Cell("A4").Value = "Categorías de la semana"; sheet.Cell("A4").Style.Font.FontSize = 10; sheet.Cell("A4").Style.Font.FontColor = XLColor.FromHtml("#64748B"); sheet.Cell("A4").Style.Alignment.Indent = 1;
        sheet.Cell("A5").Value = "Categoría";
        for (int col = 2; col <= 6; col++) { sheet.Cell(5, col).Value = Days[col - 2]; sheet.Cell(4, col).FormulaA1 = $"$B$3+{col - 2}"; sheet.Cell(4, col).Style.DateFormat.Format = "dd/mm/yyyy"; }
        sheet.Range("B4:F4").Style.Font.FontColor = blue; sheet.Range("B4:F4").Style.Font.Bold = true; sheet.Range("B4:F4").Style.Protection.Hidden = true; sheet.Range("B4:F4").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center; sheet.Row(4).Height = 27;
        var headers = sheet.Range("A5:F5"); headers.Style.Fill.BackgroundColor = line; headers.Style.Font.FontColor = blue; headers.Style.Font.Bold = true; headers.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center; sheet.Row(5).Height = 30;
        for (int block = 0; block < blocks; block++) {
            int row = 6 + block * 3;
            var category = sheet.Cell(row, 1); category.Value = active[block].Name; category.Style.Protection.Locked = false;
            var validation = category.CreateDataValidation(); validation.List("=CategoriasAlmuerzo", true); validation.IgnoreBlanks = false; validation.ErrorStyle = XLErrorStyle.Stop; validation.ShowErrorMessage = true; validation.ErrorTitle = "Categoría no disponible"; validation.ErrorMessage = "Selecciona una categoría de la lista de esta regional."; validation.InputTitle = "Categoría para los cinco días"; validation.InputMessage = "Para quitar o agregar categorías completas, usa el filtro del encabezado Categoría."; validation.ShowInputMessage = true;
            // Repeat the key for the native filter, without repeating the visible label. No merged filter cells.
            for (int slot = 1; slot < 3; slot++) { var continuation = sheet.Cell(row + slot, 1); continuation.FormulaA1 = $"A{row}"; continuation.Style.Protection.Hidden = true; }
            var label = sheet.Range(row, 1, row + 2, 1); label.Style.Fill.BackgroundColor = pale; label.Style.Font.FontColor = blue; label.Style.Font.Bold = true; label.Style.Alignment.WrapText = true; label.Style.Alignment.Indent = 1;
            sheet.Range(row + 1, 1, row + 2, 1).Style.Font.FontColor = pale;
            var dishes = sheet.Range(row, 2, row + 2, 6); dishes.Style.Protection.Locked = false; dishes.Style.Fill.BackgroundColor = block % 2 == 0 ? XLColor.White : XLColor.FromHtml("#F8FAFC"); dishes.Style.Alignment.WrapText = true; dishes.Style.Alignment.Indent = 1; dishes.Style.Border.InsideBorder = XLBorderStyleValues.Hair; dishes.Style.Border.InsideBorderColor = XLColor.FromHtml("#E2E8F0");
            var dishValidation = dishes.CreateDataValidation(); dishValidation.TextLength.EqualOrLessThan(300); dishValidation.IgnoreBlanks = true; dishValidation.ErrorStyle = XLErrorStyle.Stop; dishValidation.ShowErrorMessage = true; dishValidation.ErrorTitle = "Plato demasiado largo"; dishValidation.ErrorMessage = "Usa hasta 300 caracteres por plato."; dishValidation.InputTitle = "Un plato por casilla"; dishValidation.InputMessage = "Hasta tres platos por categoría y día. Deja vacías las casillas que no ofreces."; dishValidation.ShowInputMessage = true;
            sheet.Rows(row, row + 2).Height = 36;
            var blockRange = sheet.Range(row, 1, row + 2, 6); blockRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin; blockRange.Style.Border.OutsideBorderColor = line;
        }
        // Filtering a single column hides entire worksheet rows, including all five days.
        sheet.Range(5, 1, lastRow, 1).SetAutoFilter();
        sheet.ShowGridLines = false; sheet.SheetView.FreezeRows(5); sheet.SheetView.ZoomScale = 85; date.SetActive(); sheet.SelectedRanges.Add(sheet.Range("B3"));
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape; sheet.PageSetup.PaperSize = XLPaperSize.A4Paper; sheet.PageSetup.FitToPages(1, 0); sheet.PageSetup.SetRowsToRepeatAtTop(1, 5); sheet.PageSetup.PrintAreas.Add($"A1:F{lastRow}"); sheet.PageSetup.CenterHorizontally = true;
        sheet.Protect(TemplateProtectionPassword, XLProtectionAlgorithm.Algorithm.SHA512, allowedElements: XLSheetProtectionElements.SelectUnlockedCells | XLSheetProtectionElements.AutoFilter);
        meta.Protect(TemplateProtectionPassword, XLProtectionAlgorithm.Algorithm.SHA512, allowedElements: XLSheetProtectionElements.None);
        meta.Visibility = XLWorksheetVisibility.VeryHidden;
        book.Protect(TemplateProtectionPassword, XLProtectionAlgorithm.Algorithm.SHA512, allowedElements: XLWorkbookProtectionElements.Windows);
        using var output = new MemoryStream(); book.SaveAs(output, new SaveOptions { EvaluateFormulasBeforeSaving = true }); return output.ToArray();
    }
}
