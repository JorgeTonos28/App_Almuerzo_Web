using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Almuerzo.Web.Data;
using Almuerzo.Web.Models;
using Almuerzo.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Almuerzo.Web.Controllers;

public class LoginModel
{
    [Required, MaxLength(254)] public string Username { get; set; } = "";
    [Required, MaxLength(256), DataType(DataType.Password)] public string Password { get; set; } = "";
}
public class AccessModel
{
    public string Code { get; set; } = "";
    [MaxLength(150)] public string SuggestedDepartment { get; set; } = "";
}

public class AuthController(IDirectoryIdentityProvider directory, AppDbContext db, LunchService lunch, BrandingService branding) : Controller
{
    [AllowAnonymous, HttpGet] public async Task<IActionResult> Login() { ViewBag.Branding = await branding.Get(); return View(new LoginModel()); }
    [AllowAnonymous, HttpPost, EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginModel model, CancellationToken ct)
    {
        ViewBag.Branding = await branding.Get();
        if (!ModelState.IsValid) return View(model);
        DirectoryIdentity? identity;
        try { identity = await directory.AuthenticateAsync(model.Username.Trim(), model.Password, ct); }
        catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); model.Password = ""; return View(model); }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or InvalidOperationException) { ModelState.AddModelError("", "El servicio de identidad no está disponible. Intenta nuevamente."); model.Password = ""; return View(model); }
        if (identity == null) { ModelState.AddModelError("", "Credenciales inválidas."); model.Password = ""; return View(model); }
        var user = await db.Users.Include(x => x.Department).SingleOrDefaultAsync(x => x.Email == identity.Email, ct);
        if (user?.EmployeeCode is { Length: > 0 } code && code != identity.EmployeeCode || await db.Users.AnyAsync(x => x.EmployeeCode == identity.EmployeeCode && x.Email != identity.Email, ct))
        { ModelState.AddModelError("", "Los datos institucionales requieren revisión administrativa. Contacta al administrador."); model.Password = ""; return View(model); }
        if (user != null) { user.Name = identity.Name; user.EmployeeCode = identity.EmployeeCode; await db.SaveChangesAsync(ct); }
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, identity.Email), new(ClaimTypes.Email, identity.Email), new(ClaimTypes.Name, identity.Name), new("directory_code", identity.EmployeeCode) };
        if (LunchService.Enabled(user)) { claims.Add(new Claim(ClaimTypes.Role, user!.Role)); claims.Add(new Claim("enabled", "true")); claims.Add(new Claim("department", user.DepartmentId!)); }
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)), new AuthenticationProperties { IsPersistent = false });
        return RedirectToAction(LunchService.Enabled(user) ? "Index" : "Denied", LunchService.Enabled(user) ? "Home" : "Auth");
    }
    [Authorize, HttpGet] public async Task<IActionResult> Denied()
    {
        ViewBag.Branding = await branding.Get();
        var user = await db.Users.Include(x => x.Department).SingleOrDefaultAsync(x => x.Email == User.FindFirstValue(ClaimTypes.Email));
        if (LunchService.Enabled(user)) return RedirectToAction("Index", "Home");
        ViewBag.Pending = user?.Status == "PENDIENTE"; ViewBag.Inactive = user?.Status == "INACTIVO"; ViewBag.Departments = await db.Departments.Where(x => x.Status == "ACTIVO").OrderBy(x => x.Name).ToListAsync(); return View(new AccessModel { Code = User.FindFirstValue("directory_code") ?? "", SuggestedDepartment = user?.Department?.Name ?? "" });
    }
    [Authorize, HttpPost, EnableRateLimiting("login")] public async Task<IActionResult> RequestAccess(AccessModel model)
    {
        model.Code = User.FindFirstValue("directory_code") ?? "";
        if (!ModelState.IsValid || !System.Text.RegularExpressions.Regex.IsMatch(model.Code, "^[0-9]{4}$")) { TempData["Message"] = "La identidad o el departamento requieren revisión. Inicia sesión nuevamente."; return RedirectToAction(nameof(Denied)); }
        var email = User.FindFirstValue(ClaimTypes.Email)!; var existing = await db.Users.FindAsync(email);
        if (existing?.Status == "PENDIENTE") { TempData["Message"] = "Tu solicitud está pendiente de revisión."; return RedirectToAction(nameof(Denied)); }
        if (existing != null && LunchService.Enabled(await db.Users.Include(x => x.Department).SingleAsync(x => x.Email == email))) return RedirectToAction("Index", "Home");
        if (await db.Users.AnyAsync(x => x.EmployeeCode == model.Code && x.Email != email)) { TempData["Message"] = "Ese código de empleado ya está registrado."; return RedirectToAction(nameof(Denied)); }
        if (existing == null) db.Users.Add(new AppUser { Email = email, Name = User.Identity!.Name!, EmployeeCode = model.Code, Status = "PENDIENTE", DepartmentId = null, Role = Roles.User });
        else { existing.Status = "PENDIENTE"; existing.EmployeeCode = model.Code; }
        var admins = (await db.Users.Where(x => x.Role == Roles.GeneralAdmin && x.Status == "ACTIVO" && x.Department!.Status == "ACTIVO").Select(x => x.Email).ToListAsync()).Concat(LunchService.Split((await lunch.Config()).GetValueOrDefault("ADMIN_EMAILS", ""))).Distinct();
        foreach (var admin in admins) lunch.Queue(admin, "Nueva solicitud de acceso a Almuerzo", $"{User.Identity!.Name} ({email}) solicita acceso. Código: {model.Code}. Departamento sugerido: {model.SuggestedDepartment}. Revisa la solicitud y asigna rol y departamento.");
        lunch.Queue(email, "Solicitud de acceso recibida", "Tu solicitud fue recibida y está pendiente de revisión administrativa.");
        db.Audit.Add(new AuditEvent { Actor = email, Action = "RequestAccess", EntityId = email });
        try { await db.SaveChangesAsync(); } catch (DbUpdateException) { TempData["Message"] = "La solicitud ya está registrada o el código no está disponible."; }
        return RedirectToAction(nameof(Denied));
    }
    [Authorize, HttpPost] public async Task<IActionResult> Logout() { await HttpContext.SignOutAsync(); return RedirectToAction(nameof(Login)); }
}
