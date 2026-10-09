using System.Security.Claims;
using System.Text.Json;
using Almuerzo.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Almuerzo.Web.Controllers;

[Authorize(Policy = "Enabled")]
public class HomeController(LunchService lunch, IAntiforgery csrf, IWebHostEnvironment env, BrandingService branding) : Controller
{
    [HttpGet] public async Task<IActionResult> Index()
    {
        var data = JsonSerializer.SerializeToNode(await lunch.Init(await lunch.Actor(User.FindFirstValue(ClaimTypes.Email)!)))!.AsObject();
        var appearance = await branding.Get();
        data["branding"] = JsonSerializer.SerializeToNode(appearance, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var html = await System.IO.File.ReadAllTextAsync(Path.Combine(env.ContentRootPath, "Templates", "app.html"));
        html = html.Replace("__FAVICON__", System.Net.WebUtility.HtmlEncode(appearance.FaviconUrl)).Replace("__CSRF__", csrf.GetAndStoreTokens(HttpContext).RequestToken).Replace("__NONCE__", HttpContext.Items["nonce"]?.ToString()).Replace("__INITIAL_JSON__", JsonSerializer.Serialize(data));
        return Content(html, "text/html; charset=utf-8");
    }
}
