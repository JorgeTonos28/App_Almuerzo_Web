using System.Security.Claims;
using System.Text.Json;
using Almuerzo.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Almuerzo.Web.Controllers;

[Authorize(Policy = "Enabled")]
public class HomeController(LunchService lunch, IAntiforgery csrf, IWebHostEnvironment env, IConfiguration config) : Controller
{
    [HttpGet] public async Task<IActionResult> Index()
    {
        var data = JsonSerializer.SerializeToNode(await lunch.Init(await lunch.Actor(User.FindFirstValue(ClaimTypes.Email)!)))!.AsObject();
        var signature = config["Branding:FooterSignaturePath"] ?? "";
        if (!signature.StartsWith("/assets/branding/", StringComparison.Ordinal) || signature.Contains("..") || !new[] { ".png", ".jpg", ".jpeg", ".webp" }.Contains(Path.GetExtension(signature).ToLowerInvariant()) || !System.IO.File.Exists(Path.Combine(env.WebRootPath, signature.TrimStart('/')))) signature = "";
        data["footerSignatureUrl"] = signature;
        var html = await System.IO.File.ReadAllTextAsync(Path.Combine(env.ContentRootPath, "Templates", "app.html"));
        html = html.Replace("__INITIAL_JSON__", JsonSerializer.Serialize(data)).Replace("__CSRF__", csrf.GetAndStoreTokens(HttpContext).RequestToken).Replace("__NONCE__", HttpContext.Items["nonce"]?.ToString());
        return Content(html, "text/html; charset=utf-8");
    }
}
