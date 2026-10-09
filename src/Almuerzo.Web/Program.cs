using System.Security.Claims;
using System.Threading.RateLimiting;
using Almuerzo.Web.Data;
using Almuerzo.Web.Models;
using Almuerzo.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography.X509Certificates;

var builder = WebApplication.CreateBuilder(args);
if (args.Contains("--demo")) {
    if (!builder.Environment.IsDevelopment()) throw new InvalidOperationException("El modo demo solo está permitido en Development.");
    if (args.Contains("--import")) throw new InvalidOperationException("La importación y la demostración deben usar almacenes independientes.");
    var demoPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "demo"); Directory.CreateDirectory(demoPath);
    var passwordPath = Path.Combine(demoPath, "access.txt");
    var password = File.Exists(passwordPath) ? File.ReadAllText(passwordPath).Trim() : Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(12));
    File.WriteAllText(passwordPath, password);
    var email = "demo.almuerzo@infotep.gob.do";
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Path"] = demoPath, ["ConnectionStrings:DefaultConnection"] = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = Path.Combine(demoPath, "almuerzo.db"), DefaultTimeout = 30 }.ToString(), ["DevelopmentIdentity:Enabled"] = "true", ["DevelopmentIdentity:Email"] = email, ["DevelopmentIdentity:Name"] = "Demostración local", ["DevelopmentIdentity:PasswordHash"] = new Microsoft.AspNetCore.Identity.PasswordHasher<string>().HashPassword(email, password), ["Operations:Enabled"] = "false" });
}
var dataPath = Path.GetFullPath(builder.Configuration["Storage:Path"] ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data"));
Directory.CreateDirectory(dataPath);
builder.Services.AddSingleton<StoragePaths>(sp => {
    var c = sp.GetRequiredService<IConfiguration>(); var root = Path.GetFullPath(c["Storage:Path"] ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data")); Directory.CreateDirectory(root); return new StoragePaths(root);
});
var protection = builder.Services.AddDataProtection().SetApplicationName("Almuerzo.Web").PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataPath, "keys")));
if (!string.IsNullOrWhiteSpace(builder.Configuration["DataProtection:CertificatePath"]))
    protection.ProtectKeysWithCertificate(X509CertificateLoader.LoadPkcs12FromFile(builder.Configuration["DataProtection:CertificatePath"]!, builder.Configuration["DataProtection:CertificatePassword"]));
else if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
else if (!builder.Environment.IsDevelopment()) throw new InvalidOperationException("Producción requiere un certificado para cifrar las claves de Data Protection en este sistema operativo.");
builder.Services.AddDbContext<AppDbContext>((sp, o) => o.UseSqlite(sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection") ?? new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = Path.Combine(sp.GetRequiredService<StoragePaths>().Root, "almuerzo.db"), DefaultTimeout = 30 }.ToString()));
builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddAntiforgery(o => { o.HeaderName = "X-CSRF-TOKEN"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Strict; o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always; });
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<BusinessRules>(); builder.Services.AddScoped<LunchService>(); builder.Services.AddScoped<ReportService>(); builder.Services.AddScoped<WorkbookImporter>();
builder.Services.AddHttpClient<IDirectoryIdentityProvider, DirectoryIdentityProvider>(c => c.Timeout = TimeSpan.FromSeconds(12)).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o => {
    o.LoginPath = "/Auth/Login"; o.AccessDeniedPath = "/Auth/Denied"; o.Cookie.Name = "Almuerzo.Session"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromHours(8); o.SlidingExpiration = false;
    o.Events.OnValidatePrincipal = async context => {
        var email = context.Principal?.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email)) { context.RejectPrincipal(); return; }
        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var u = await db.Users.AsNoTracking().Include(x => x.Department).SingleOrDefaultAsync(x => x.Email == email);
        var identity = new ClaimsIdentity(context.Principal!.Identity);
        foreach (var claim in identity.FindAll(ClaimTypes.Role).Concat(identity.FindAll("enabled")).Concat(identity.FindAll("department")).ToArray()) identity.RemoveClaim(claim);
        if (LunchService.Enabled(u)) { identity.AddClaim(new Claim(ClaimTypes.Role, u!.Role)); identity.AddClaim(new Claim("enabled", "true")); identity.AddClaim(new Claim("department", u.DepartmentId!)); }
        context.ReplacePrincipal(new ClaimsPrincipal(identity));
    };
    o.Events.OnRedirectToLogin = c => { if (c.Request.Path.StartsWithSegments("/api")) c.Response.StatusCode = 401; else c.Response.Redirect(c.RedirectUri); return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { if (c.Request.Path.StartsWithSegments("/api")) c.Response.StatusCode = 403; else c.Response.Redirect(c.RedirectUri); return Task.CompletedTask; };
});
builder.Services.AddAuthorization(o => {
    o.AddPolicy("Enabled", p => p.RequireAuthenticatedUser().RequireClaim("enabled", "true"));
    o.AddPolicy("GeneralAdmin", p => p.RequireAuthenticatedUser().RequireClaim("enabled", "true").RequireRole(Roles.GeneralAdmin));
});
builder.Services.AddRateLimiter(o => {
    o.RejectionStatusCode = 429;
    o.AddPolicy("login", c => RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
    o.AddPolicy("rpc", c => RateLimitPartition.GetFixedWindowLimiter(c.User.FindFirstValue(ClaimTypes.Email) ?? c.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 180, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddHostedService<OperationsWorker>();
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
var app = builder.Build();
using (var scope = app.Services.CreateScope()) {
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); await db.Database.MigrateAsync();
    if (args.Contains("--import")) {
        var path = args.SkipWhile(x => x != "--import").Skip(1).FirstOrDefault() ?? throw new ArgumentException("Se requiere la ruta del Excel.");
        var result = await scope.ServiceProvider.GetRequiredService<WorkbookImporter>().ImportAsync(path);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result)); await SeedData.InitializeAsync(db); return;
    }
    await SeedData.InitializeAsync(db);
    if (args.Contains("--demo")) await DemoData.InitializeAsync(db, scope.ServiceProvider.GetRequiredService<BusinessRules>());
}
if (!app.Environment.IsDevelopment()) { app.UseHsts(); app.UseHttpsRedirection(); }
app.UseExceptionHandler(handler => handler.Run(async c => { c.Response.StatusCode = 500; await c.Response.WriteAsJsonAsync(new { ok = false, msg = "Ocurrió un error. Intenta nuevamente o contacta al administrador.", traceId = c.TraceIdentifier }); }));
app.Use(async (context, next) => {
    var nonce = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18)); context.Items["nonce"] = nonce;
    context.Response.Headers["X-Content-Type-Options"] = "nosniff"; context.Response.Headers["Referrer-Policy"] = "same-origin"; context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    // Vue templates are compiled during the build; runtime eval is unnecessary.
    context.Response.Headers["Content-Security-Policy"] = $"default-src 'self'; script-src 'self' 'nonce-{nonce}'; style-src 'self' 'unsafe-inline'; font-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";
    if (!context.Request.Path.StartsWithSegments("/assets") && !context.Request.Path.StartsWithSegments("/lib")) context.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseStaticFiles(); app.UseRouting(); app.UseAuthentication(); app.UseRateLimiter(); app.UseAuthorization();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
app.Run();
public partial class Program;
