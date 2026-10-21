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

public class AuthController(IDirectoryIdentityProvider directory, AppDbContext db, LunchService lunch, BrandingService branding, DirectoryOrganization organization) : Controller
{
    [AllowAnonymous, HttpGet] public async Task<IActionResult> Login() { ViewBag.Branding = await branding.Get(); return View(new LoginModel()); }
    [AllowAnonymous, HttpPost, EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginModel model, CancellationToken ct)
    {
        ViewBag.Branding = await branding.Get();
        if (!ModelState.IsValid) return LoginFailure(model);
        DirectoryIdentity? identity;
        try { identity = await directory.AuthenticateAsync(model.Username.Trim(), model.Password, ct); }
        catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); model.Password = ""; return LoginFailure(model); }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or InvalidOperationException) { ModelState.AddModelError("", "El servicio de identidad no está disponible. Intenta nuevamente."); model.Password = ""; return LoginFailure(model); }
        if (identity == null) { ModelState.AddModelError("", "Credenciales inválidas."); model.Password = ""; return LoginFailure(model); }
        // The directory supplies employee code, not a verified email address. Preserve legacy contact/history.
        var candidates = await db.Users.IgnoreQueryFilters().Include(x => x.Department).Include(x => x.DirectoryDepartment).Where(x => x.DirectoryAccount == identity.Email || x.EmployeeCode == identity.EmployeeCode || x.Email == identity.Email).ToListAsync(ct);
        var user = candidates.Count == 1 ? candidates[0] : null;
        if (candidates.Count > 1 || user?.EmployeeCode is { Length: > 0 } code && code != identity.EmployeeCode || user?.DirectoryAccount is { Length: > 0 } account && account != identity.Email)
        { ModelState.AddModelError("", "Los datos institucionales requieren revisión administrativa. Contacta al administrador."); model.Password = ""; return LoginFailure(model); }
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try { user = await organization.Synchronize(identity, user, ct); }
        catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); model.Password = ""; return LoginFailure(model); }
        db.AllowCrossRegionWrites = true;
        if (user != null)
        {
            // An earlier session may belong to another region. New records belong to this verified profile.
            db.RegionId = user.RegionId;
            if (string.IsNullOrEmpty(user.DirectoryAccount)) db.Audit.Add(new AuditEvent { RegionId = user.RegionId, Actor = user.Email, Action = "LinkDirectoryAccount", EntityId = user.Email });
            user.Name = identity.Name; user.EmployeeCode = identity.EmployeeCode; user.DirectoryAccount = identity.Email;
            if (identity.JobTitle != null) user.JobTitle = identity.JobTitle;
            try { await db.SaveChangesAsync(ct); await organization.InitializeCreatedRegion(); }
            catch (DbUpdateException) { ModelState.AddModelError("", "La vinculación institucional cambió durante el acceso. Intenta nuevamente o contacta a TI."); model.Password = ""; return LoginFailure(model); }
        }
        await transaction.CommitAsync(ct); db.AllowCrossRegionWrites = false;
        var localEmail = user?.Email ?? identity.Email;
        var enabled = LunchService.Enabled(user) && await db.Regions.AnyAsync(x => x.Id == user!.RegionId && x.Status == "ACTIVO", ct);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, localEmail), new(ClaimTypes.Email, localEmail), new(ClaimTypes.Name, identity.Name), new("directory_code", identity.EmployeeCode), new("directory_account", identity.Email), new("region", user?.RegionId ?? Region.LegacyId) };
        if (enabled) { claims.Add(new Claim(ClaimTypes.Role, user!.Role)); claims.Add(new Claim("enabled", "true")); claims.Add(new Claim("department", user.DepartmentId!)); }
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)), new AuthenticationProperties { IsPersistent = false });
        var destination = Url.Action(enabled ? "Index" : "Denied", enabled ? "Home" : "Auth")!;
        if (WantsJson) return Json(new { ok = true, redirectUrl = destination });
        return LocalRedirect(destination);
    }
    private bool WantsJson => Request.Headers.Accept.Any(x => x != null && x.Contains("application/json", StringComparison.OrdinalIgnoreCase));
    private IActionResult LoginFailure(LoginModel model)
    {
        model.Password = "";
        if (WantsJson) return Json(new { ok = false, msg = string.Join(" ", ModelState.Values.SelectMany(x => x.Errors).Select(x => x.ErrorMessage).Where(x => x.Length > 0)) });
        return View("Login", model);
    }
    [Authorize, HttpGet] public async Task<IActionResult> Denied()
    {
        ViewBag.Branding = await branding.Get();
        var user = await db.Users.IgnoreQueryFilters().Include(x => x.Department).Include(x => x.DirectoryDepartment).SingleOrDefaultAsync(x => x.Email == User.FindFirstValue(ClaimTypes.Email));
        var activeRegion = await db.Regions.AnyAsync(x => x.Id == db.RegionId && x.Status == "ACTIVO");
        if (LunchService.Enabled(user) && activeRegion) return RedirectToAction("Index", "Home");
        ViewBag.RegionalBlocked = user?.RegionalBlocked == true; ViewBag.Pending = user?.Status == "PENDIENTE"; ViewBag.Inactive = user?.Status == "INACTIVO" || !activeRegion; ViewBag.Departments = await db.Departments.Where(x => x.Status == "ACTIVO").OrderBy(x => x.Name).ToListAsync(); return View(new AccessModel { Code = User.FindFirstValue("directory_code") ?? "", SuggestedDepartment = user?.Department?.DisplayName ?? "" });
    }
    [Authorize, HttpPost, EnableRateLimiting("login")] public async Task<IActionResult> RequestAccess(AccessModel model)
    {
        model.Code = User.FindFirstValue("directory_code") ?? "";
        if (!ModelState.IsValid || !System.Text.RegularExpressions.Regex.IsMatch(model.Code, "^[0-9]{4}$")) { TempData["Message"] = "La identidad o el departamento requieren revisión. Inicia sesión nuevamente."; return RedirectToAction(nameof(Denied)); }
        var email = User.FindFirstValue(ClaimTypes.Email)!; var existing = await db.Users.FindAsync(email);
        if (existing?.Status == "INACTIVO" || existing?.RegionalBlocked == true) { TempData["Message"] = "Tu acceso está suspendido. Contacta a la administración; una solicitud no puede retirar el bloqueo."; return RedirectToAction(nameof(Denied)); }
        if (existing?.Status == "PENDIENTE") { TempData["Message"] = "Tu solicitud está pendiente de revisión."; return RedirectToAction(nameof(Denied)); }
        if (existing?.DirectoryManaged == true && existing.DepartmentId == null) { TempData["Message"] = "Tu identidad institucional ya está registrada. Un administrador general debe asignarte un grupo de almuerzo."; return RedirectToAction(nameof(Denied)); }
        if (existing != null && LunchService.Enabled(await db.Users.Include(x => x.Department).Include(x => x.DirectoryDepartment).SingleAsync(x => x.Email == email))) return RedirectToAction("Index", "Home");
        if (await db.Users.IgnoreQueryFilters().AnyAsync(x => x.EmployeeCode == model.Code && x.Email != email)) { TempData["Message"] = "Ese código de empleado ya está registrado."; return RedirectToAction(nameof(Denied)); }
        if (existing == null) db.Users.Add(new AppUser { Email = email, Name = User.Identity!.Name!, EmployeeCode = model.Code, DirectoryAccount = User.FindFirstValue("directory_account"), Status = "PENDIENTE", DepartmentId = null, Role = Roles.User });
        else { existing.Status = "PENDIENTE"; existing.EmployeeCode = model.Code; }
        var admins = (await db.Users.IgnoreQueryFilters().Where(x => (x.Role == Roles.GeneralAdmin || x.Role == Roles.RegionalAdmin && x.RegionId == db.RegionId) && x.Status == "ACTIVO" && x.Department!.Status == "ACTIVO").Select(x => x.Email).ToListAsync()).Concat(LunchService.Split((await lunch.Config()).GetValueOrDefault("ADMIN_EMAILS", ""))).Distinct();
        foreach (var admin in admins) lunch.Queue(admin, "Nueva solicitud de acceso a Almuerzo", $"{User.Identity!.Name} ({email}) solicita acceso. Código: {model.Code}. Departamento sugerido: {model.SuggestedDepartment}. Revisa la solicitud y asigna rol y departamento.");
        lunch.Queue(email, "Solicitud de acceso recibida", "Tu solicitud fue recibida y está pendiente de revisión administrativa.");
        db.Audit.Add(new AuditEvent { Actor = email, Action = "RequestAccess", EntityId = email });
        try { await db.SaveChangesAsync(); } catch (DbUpdateException) { TempData["Message"] = "La solicitud ya está registrada o el código no está disponible."; }
        return RedirectToAction(nameof(Denied));
    }
    [Authorize, HttpPost] public async Task<IActionResult> Logout() { await HttpContext.SignOutAsync(); return RedirectToAction(nameof(Login)); }
}
