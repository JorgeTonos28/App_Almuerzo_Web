using System.Security.Claims;
using Almuerzo.Web.Data;
using Almuerzo.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Almuerzo.Web.Controllers;

[Authorize(Policy = "RegionalManagement"), EnableRateLimiting("menu-workbook"), Route("menu/workbook")]
public class MenuWorkbookController(AppDbContext db, LunchService lunch, BusinessRules rules, ILogger<MenuWorkbookController> logger) : Controller
{
    [HttpGet("template")]
    public async Task<IActionResult> Template(string? weekStart, CancellationToken ct)
    {
        try {
            LunchService.ManageRegion(await lunch.Actor(User.FindFirstValue(ClaimTypes.Email)!));
            var today = BusinessRules.Date(rules.Today); var nextMonday = today.AddDays(((8 - (int)today.DayOfWeek) % 7) is 0 ? 7 : (8 - (int)today.DayOfWeek) % 7);
            var week = string.IsNullOrWhiteSpace(weekStart) ? nextMonday.ToString("yyyy-MM-dd") : weekStart;
            var categories = await db.Categories.AsNoTracking().ToListAsync(ct); var region = await db.Regions.SingleAsync(r => r.Id == db.RegionId, ct);
            Response.Headers.CacheControl = "no-store";
            return File(MenuWorkbook.Template(week, categories, db.RegionId, region.DisplayName), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"plantilla-menu-{week}.xlsx");
        } catch (BusinessException ex) { return BadRequest(new { ok = false, msg = ex.Message }); }
    }

    [HttpPost("preview"), RequestSizeLimit(2_100_000), RequestFormLimits(MultipartBodyLengthLimit = 2_100_000)]
    public async Task<IActionResult> Preview(IFormFile? file, [FromForm] string? weekStart, CancellationToken ct)
    {
        try {
            LunchService.ManageRegion(await lunch.Actor(User.FindFirstValue(ClaimTypes.Email)!));
            LunchService.Require(ModelState.IsValid && file is { Length: > 0 and <= MenuWorkbook.MaxBytes }, "Selecciona un Excel .xls o .xlsx de hasta 2 MB.");
            await using var memory = new MemoryStream(); await file!.CopyToAsync(memory, ct);
            var categories = await db.Categories.AsNoTracking().ToListAsync(ct);
            var holidays = await lunch.Holidays();
            MenuWorkbookPreview result;
            try { result = MenuWorkbook.Preview(memory.ToArray(), file.FileName, weekStart, categories, db.RegionId, holidays, rules.Today, ct); }
            catch (BusinessException) { throw; }
            catch (Exception ex) when (ex is not OperationCanceledException) {
                // Do not retain or log cell values, filenames, provider payloads or the uploaded workbook.
                logger.LogWarning("Se rechazó un libro de menú no legible ({ErrorType}).", ex.GetType().Name);
                throw new BusinessException("No se pudo leer el Excel. Usa un archivo válido, sin contraseña, o descarga una nueva plantilla.");
            }
            Response.Headers.CacheControl = "no-store";
            return Json(new { ok = true, result });
        } catch (BusinessException ex) { return BadRequest(new { ok = false, msg = ex.Message }); }
    }
}
