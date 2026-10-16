using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Almuerzo.Web.Data;
using Almuerzo.Web.Models;
using Almuerzo.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Almuerzo.Web.Controllers;

public class ReportsController(ReportService reports, LunchService lunch, AppDbContext db, IConfiguration config) : Controller
{
    [Authorize(Policy = "Enabled", Roles = Roles.GeneralAdmin + "," + Roles.RegionalAdmin + "," + Roles.DepartmentAdmin), HttpGet("reports/daily/{date}")]
    public async Task<IActionResult> Daily(string date, string format = "xlsx")
    {
        var actor = await lunch.Actor(User.FindFirstValue(ClaimTypes.Email)!); LunchService.Admin(actor);
        try {
            var file = await reports.GenerateAsync(date, actor.Role == Roles.DepartmentAdmin ? actor.DepartmentId : null, format);
            return PhysicalFile(file, format == "pdf" ? "application/pdf" : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"almuerzo-{date}.{format}");
        } catch (BusinessException ex) { return BadRequest(new { ok = false, msg = ex.Message }); }
    }
    [AllowAnonymous, HttpGet("api/menu-day/{date}")]
    public async Task<IActionResult> MenuDay(string date, string regionalId = Region.LegacyId)
    {
        var expected = config["Integrations:MenuToken"] ?? ""; var actual = Request.Headers.Authorization.ToString();
        if (expected.Length == 0 || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes("Bearer " + expected)), SHA256.HashData(Encoding.UTF8.GetBytes(actual)))) return Unauthorized();
        if (!await db.Regions.AnyAsync(x => x.Id == regionalId && x.Status == "ACTIVO")) return BadRequest();
        db.RegionId = regionalId;
        try { BusinessRules.Date(date); } catch (BusinessException) { return BadRequest(); }
        var rows = await db.Menu.AsNoTracking().Where(x => x.Date == date && x.Enabled).ToListAsync();
        return Json(new { ok = true, fecha = date, date, exists = rows.Count > 0, existeMenu = rows.Count > 0, appVersion = "0.2.0", items = rows.Select(x => new { id = x.Id, categoria = x.CategoryId, plato = BusinessRules.Text(x.Dish), descripcion = BusinessRules.Text(x.Description) }) });
    }
}
