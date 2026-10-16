using Almuerzo.Web.Data;
using Almuerzo.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Almuerzo.Web.Controllers;

[Authorize(Policy = "GeneralAdmin"), Route("regions")]
public class RegionsController(AppDbContext db) : Controller
{
    [HttpPost("select")]
    public async Task<IActionResult> Select(string regionId)
    {
        if (!await db.Regions.AnyAsync(x => x.Id == regionId && x.Status == "ACTIVO")) return BadRequest();
        Response.Cookies.Append("Almuerzo.Region", regionId, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = Request.IsHttps, MaxAge = TimeSpan.FromHours(8) });
        return LocalRedirect("/?admin=1");
    }
}
