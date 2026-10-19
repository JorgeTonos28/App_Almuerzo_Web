using Almuerzo.Web.Data;
using Almuerzo.Web.Models;
using Microsoft.EntityFrameworkCore;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace Almuerzo.Web.Services;

public record ReportColumn(string Id, string Name);
public record ReportPerson(string Name, string Code, string Department, Dictionary<string, string> Dishes, string Note);
public record ReportSection(string Name, List<ReportPerson> People);
public record ReportData(string Date, List<ReportColumn> Categories, List<ReportSection> Sections, string RegionName = "");

public class ReportFontResolver : IFontResolver
{
    private readonly string root = Path.Combine(AppContext.BaseDirectory, "Fonts");
    public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) => new(bold ? "Carlito-Bold" : "Carlito-Regular", false, italic);
    public byte[] GetFont(string faceName) => File.ReadAllBytes(Path.Combine(root, faceName + ".ttf"));
    private static readonly object Gate = new();
    public static void Initialize() { lock (Gate) { if (GlobalFontSettings.FontResolver == null) GlobalFontSettings.FontResolver = new ReportFontResolver(); } }
}

public static class ReportLayout
{
    public static async Task<ReportData> Load(AppDbContext db, string date, string? department)
    {
        BusinessRules.Date(date);
        var orders = await db.Orders.AsNoTracking().Where(x => x.Date == date && x.Status == "ACTIVO" && (department == null || x.DepartmentId == department)).OrderBy(x => x.Name).ToListAsync();
        var departments = await db.Departments.ToDictionaryAsync(x => x.Id, x => x.Alias != "" ? x.Alias : x.Name);
        // A directory transfer must not remove employee codes from reports of earlier orders.
        var orderedEmails = orders.Select(x => x.Email).Distinct().ToArray();
        var codes = await db.Users.IgnoreQueryFilters().Where(x => orderedEmails.Contains(x.Email)).ToDictionaryAsync(x => x.Email, x => x.EmployeeCode ?? "");
        var categories = (await db.Categories.OrderBy(x => x.SortOrder).ToListAsync()).Select(c => new ReportColumn(c.Id, c.Name)).ToList();
        foreach (var o in orders) foreach (var id in LunchService.List(LunchService.Obj(o.DetailJson)["categorias"])) if (categories.All(c => c.Id != id)) categories.Add(new(id, id.Replace('_', ' ')));
        ReportPerson Person(Order o) {
            var detail = LunchService.Obj(o.DetailJson); var cats = LunchService.List(detail["categorias"]); var items = LunchService.List(detail["items"]); var dishes = new Dictionary<string, string>();
            for (var i = 0; i < Math.Min(cats.Length, items.Length); i++) dishes[cats[i]] = string.Join(", ", new[] { dishes.GetValueOrDefault(cats[i], ""), BusinessRules.Text(items[i]) }.Where(x => x.Length > 0));
            return new(o.Name, codes.GetValueOrDefault(o.Email, ""), departments.GetValueOrDefault(o.DepartmentId, o.DepartmentId), dishes, LunchService.Str(detail, "comentarios"));
        }
        var sections = new List<ReportSection> { new(department == null ? "Resumen general" : departments.GetValueOrDefault(department, department), orders.Select(Person).ToList()) };
        if (department == null) foreach (var group in orders.GroupBy(x => x.DepartmentId)) sections.Add(new(departments.GetValueOrDefault(group.Key, group.Key), group.Select(Person).ToList()));
        return new(date, categories, sections, (await db.Regions.FindAsync(db.RegionId))?.DisplayName ?? db.RegionId);
    }
    public static string[] Headers(IEnumerable<ReportColumn> cats) => new[] { "NO.", "NOMBRE EMPLEADO", "CÓDIGO", "DEPARTAMENTO" }.Concat(cats.Select(c => c.Name.ToUpperInvariant())).Append("NOTA PARA LA COCINA").ToArray();
    public static string[] Values(ReportPerson p, int number, IEnumerable<ReportColumn> cats) => new[] { number.ToString(), p.Name, p.Code, p.Department }.Concat(cats.Select(c => p.Dishes.GetValueOrDefault(c.Id, ""))).Append(p.Note).ToArray();
    public static void Pdf(ReportData data, string file)
    {
        ReportFontResolver.Initialize();
        var document = new Document(); document.Info.Title = $"Pedido de almuerzo {data.Date}";
        var normal = document.Styles[StyleNames.Normal]!; normal.Font.Name = "Carlito"; normal.Font.Size = 8;
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(2);
        foreach (var block in data.Sections)
        {
            // Split category columns across pages to keep kitchen reports readable.
            var groups = data.Categories.Chunk(5).ToArray();
            foreach (var cats in groups.Length == 0 ? new[] { Array.Empty<ReportColumn>() } : groups)
            {
                var section = document.AddSection(); section.PageSetup.PageFormat = PageFormat.A4; section.PageSetup.Orientation = Orientation.Landscape;
                section.PageSetup.TopMargin = Unit.FromCentimeter(1.2); section.PageSetup.BottomMargin = Unit.FromCentimeter(1.2); section.PageSetup.LeftMargin = Unit.FromCentimeter(1.2); section.PageSetup.RightMargin = Unit.FromCentimeter(1.2);
                var heading = section.AddParagraph(block.Name); heading.Format.Font.Size = 16; heading.Format.Font.Bold = true; heading.Format.Font.Color = Colors.DarkBlue;
                section.AddParagraph($"{data.RegionName} · PEDIDO ALMUERZO: {BusinessRules.Date(data.Date):dd/MM/yyyy}  |  {block.People.Count} colaboradores").Format.SpaceAfter = Unit.FromPoint(10);
                var footer = section.Footers.Primary.AddParagraph("Almuerzo - INFOTEP  |  "); footer.AddPageField(); footer.AddText(" / "); footer.AddNumPagesField(); footer.Format.Alignment = ParagraphAlignment.Right; footer.Format.Font.Size = 8;
                var table = section.AddTable(); table.Borders.Width = Unit.FromPoint(.4); table.Borders.Color = Colors.LightGray; table.TopPadding = Unit.FromPoint(4); table.BottomPadding = Unit.FromPoint(4);
                var widths = new[] { 8d, 39, 14, 35 }.Concat(cats.Select(_ => 23d)).Append(38d).ToArray(); var scale = 273 / widths.Sum();
                foreach (var width in widths) table.AddColumn(Unit.FromMillimeter(width * scale));
                var header = table.AddRow(); header.HeadingFormat = true; header.Shading.Color = Color.FromRgb(37, 99, 235); header.Format.Font.Color = Colors.White; header.Format.Font.Bold = true;
                var labels = Headers(cats); for (var c = 0; c < labels.Length; c++) header[c].AddParagraph(labels[c]);
                var number = 1;
                foreach (var person in block.People) { var row = table.AddRow(); if (number % 2 == 0) row.Shading.Color = Color.FromRgb(249, 250, 251); var values = Values(person, number++, cats); for (var c = 0; c < values.Length; c++) row[c].AddParagraph(values[c]); }
                if (block.People.Count == 0) section.AddParagraph("No hay pedidos activos para esta fecha.");
            }
        }
        var renderer = new PdfDocumentRenderer { Document = document }; renderer.RenderDocument(); renderer.PdfDocument.Save(file);
    }
}
