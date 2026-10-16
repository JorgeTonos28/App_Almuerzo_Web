using System.Security.Claims;
using Almuerzo.Web.Data;
using Almuerzo.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Almuerzo.Web.Controllers;

[Route("branding")]
public class BrandingController(BrandingService branding, AppDbContext db) : Controller
{
    [AllowAnonymous, HttpGet("{id}.png")]
    public async Task<IActionResult> Image(string id, CancellationToken ct)
    {
        if (!Guid.TryParseExact(id, "N", out _) || !await db.Settings.IgnoreQueryFilters().AnyAsync(x => (x.Key == "FAVICON_ASSET_ID" || x.Key == "FOOTER_STAMP_ASSET_ID") && x.Value == id, ct)) return NotFound();
        var bytes = await db.BrandingAssets.AsNoTracking().Where(x => x.Id == id).Select(x => x.Content).SingleOrDefaultAsync(ct);
        return bytes == null ? NotFound() : File(bytes, "image/png");
    }
    [Authorize(Policy = "RegionalManagement"), HttpPost, EnableRateLimiting("rpc"), RequestSizeLimit(3_000_000), RequestFormLimits(MultipartBodyLengthLimit = 3_000_000)]
    public async Task<IActionResult> Save([FromForm] string? footerText, IFormFile? favicon, IFormFile? stamp, [FromForm] bool resetFavicon, [FromForm] bool removeStamp, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { ok = false, msg = "Texto inválido." });
        try { return Json(new { ok = true, branding = await branding.Save(footerText ?? "", favicon, stamp, resetFavicon, removeStamp, User.FindFirstValue(ClaimTypes.Email)!, ct) }); }
        catch (BusinessException ex) { return BadRequest(new { ok = false, msg = ex.Message }); }
    }
}
