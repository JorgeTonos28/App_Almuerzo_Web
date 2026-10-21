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
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Provider"] = "Sqlite", ["Storage:Path"] = demoPath, ["ConnectionStrings:DefaultConnection"] = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = Path.Combine(demoPath, "almuerzo.db"), DefaultTimeout = 30 }.ToString(), ["Ad:AutoProvisioningEnabled"] = "false", ["Ad:AutoCreateOrganizationEnabled"] = "false", ["DevelopmentIdentity:Enabled"] = "true", ["DevelopmentIdentity:Email"] = email, ["DevelopmentIdentity:Name"] = "Demostración local", ["DevelopmentIdentity:PasswordHash"] = new Microsoft.AspNetCore.Identity.PasswordHasher<string>().HashPassword(email, password), ["Operations:Enabled"] = "false" });
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
builder.Services.AddDbContext<SqlServerAppDbContext>((sp, o) => {
    var connection = sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(connection)) {
        if (!sp.GetRequiredService<IWebHostEnvironment>().IsDevelopment()) throw new InvalidOperationException("Configura ConnectionStrings:DefaultConnection para SQL Server antes de desplegar.");
        connection = "Server=(localdb)\\MSSQLLocalDB;Database=Almuerzo;Integrated Security=true;Encrypt=true;TrustServerCertificate=false";
    }
    o.UseSqlServer(connection);
});
builder.Services.AddScoped<AppDbContext>(sp => {
    var configuration = sp.GetRequiredService<IConfiguration>();
    var paths = sp.GetRequiredService<StoragePaths>();
    var provider = configuration["Database:Provider"] ?? "SqlServer";
    if (provider == "SqlServer") return sp.GetRequiredService<SqlServerAppDbContext>();
    if (provider != "Sqlite" || !sp.GetRequiredService<IWebHostEnvironment>().IsDevelopment()) throw new InvalidOperationException("Producción requiere SQL Server.");
    return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(configuration.GetConnectionString("DefaultConnection") ?? new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = Path.Combine(sp.GetRequiredService<StoragePaths>().Root, "almuerzo.db"), DefaultTimeout = 30 }.ToString()).Options);
});
builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddAntiforgery(o => { o.HeaderName = "X-CSRF-TOKEN"; o.Cookie.Name = "Almuerzo.Antiforgery.v2"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Strict; o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always; });
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<BusinessRules>(); builder.Services.AddScoped<LunchService>(); builder.Services.AddScoped<ReportService>(); builder.Services.AddScoped<WorkbookImporter>();
builder.Services.AddScoped<DirectoryOrganization>(); builder.Services.AddScoped<BrandingService>(); builder.Services.AddScoped<OutboxDelivery>(); builder.Services.AddSingleton<MailDeliveryPolicy>();
builder.Services.AddHttpClient<IDirectoryIdentityProvider, DirectoryIdentityProvider>(c => c.Timeout = Timeout.InfiniteTimeSpan).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHttpClient<SendGridDelivery>(c => c.Timeout = Timeout.InfiniteTimeSpan).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o => {
    o.LoginPath = "/Auth/Login"; o.AccessDeniedPath = "/Auth/Denied"; o.Cookie.Name = "Almuerzo.Session.v2"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromHours(8); o.SlidingExpiration = false;
    o.Events.OnValidatePrincipal = async context => {
        var email = context.Principal?.FindFirstValue(ClaimTypes.Email);
        var directoryCode = context.Principal?.FindFirstValue("directory_code");
        var directoryAccount = context.Principal?.FindFirstValue("directory_account");
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(directoryCode) || string.IsNullOrWhiteSpace(directoryAccount)) { context.RejectPrincipal(); return; }
        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var u = await db.Users.IgnoreQueryFilters().AsNoTracking().Include(x => x.Department).Include(x => x.DirectoryDepartment).SingleOrDefaultAsync(x => x.Email == email);
        if (u?.EmployeeCode is { Length: > 0 } code && code != directoryCode) { context.RejectPrincipal(); return; }
        if (u != null && u.DirectoryAccount != directoryAccount) { context.RejectPrincipal(); return; }
        var identity = new ClaimsIdentity(context.Principal!.Identity);
        foreach (var claim in identity.FindAll(ClaimTypes.Role).Concat(identity.FindAll("enabled")).Concat(identity.FindAll("department")).Concat(identity.FindAll("region")).ToArray()) identity.RemoveClaim(claim);
        if (u != null) identity.AddClaim(new Claim("region", u.RegionId));
        if (LunchService.Enabled(u) && await db.Regions.AnyAsync(x => x.Id == u!.RegionId && x.Status == "ACTIVO")) { identity.AddClaim(new Claim(ClaimTypes.Role, u!.Role)); identity.AddClaim(new Claim("enabled", "true")); identity.AddClaim(new Claim("department", u.DepartmentId!)); }
        context.ReplacePrincipal(new ClaimsPrincipal(identity));
    };
    o.Events.OnRedirectToLogin = c => { if (c.Request.Path.StartsWithSegments("/api") || c.Request.Path.StartsWithSegments("/branding")) c.Response.StatusCode = 401; else c.Response.Redirect(c.RedirectUri); return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { if (c.Request.Path.StartsWithSegments("/api") || c.Request.Path.StartsWithSegments("/branding") || c.Request.Path.StartsWithSegments("/regions")) c.Response.StatusCode = 403; else c.Response.Redirect(c.RedirectUri); return Task.CompletedTask; };
});
builder.Services.AddAuthorization(o => {
    o.AddPolicy("Enabled", p => p.RequireAuthenticatedUser().RequireClaim("enabled", "true"));
    o.AddPolicy("GeneralAdmin", p => p.RequireAuthenticatedUser().RequireClaim("enabled", "true").RequireRole(Roles.GeneralAdmin));
    o.AddPolicy("RegionalManagement", p => p.RequireAuthenticatedUser().RequireClaim("enabled", "true").RequireRole(Roles.GeneralAdmin, Roles.RegionalAdmin));
});
builder.Services.AddRateLimiter(o => {
    o.RejectionStatusCode = 429;
    o.AddPolicy("login", c => RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
    o.AddPolicy("rpc", c => RateLimitPartition.GetFixedWindowLimiter(c.User.FindFirstValue(ClaimTypes.Email) ?? c.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 180, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy("menu-workbook", c => RateLimitPartition.GetFixedWindowLimiter(c.User.FindFirstValue(ClaimTypes.Email) ?? c.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddHostedService<OperationsWorker>();
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
var app = builder.Build();
using (var scope = app.Services.CreateScope()) {
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); await db.Database.MigrateAsync();
    if (args.Contains("--directory-catalog")) {
        var output = args.SkipWhile(x => x != "--directory-catalog").Skip(1).FirstOrDefault() ?? throw new ArgumentException("Indica un archivo privado de salida.");
        var regions = await db.Regions.AsNoTracking().ToListAsync();
        var departments = await db.Departments.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        var directoryDepartments = await db.DirectoryDepartments.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        await File.WriteAllTextAsync(output, System.Text.Json.JsonSerializer.Serialize(regions.Select(r => new { r.Id, r.Name, r.Alias, Linked = r.DirectoryKey != null, MealGroups = departments.Where(d => d.RegionId == r.Id).Select(d => new { d.Id, d.Name, d.Alias }), DirectoryDepartments = directoryDepartments.Where(d => d.RegionId == r.Id).Select(d => new { d.Id, d.Name, d.DirectoryKey }) }), new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Catálogo organizativo exportado al archivo privado indicado. No contiene usuarios ni credenciales."); return;
    }
    if (args.Contains("--migrate-sqlite")) {
        var path = args.SkipWhile(x => x != "--migrate-sqlite").Skip(1).FirstOrDefault() ?? throw new ArgumentException("Se requiere la ruta SQLite de origen.");
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(await SqliteTransfer.Run(db, path))); return;
    }
    if (args.Contains("--import")) {
        var path = args.SkipWhile(x => x != "--import").Skip(1).FirstOrDefault() ?? throw new ArgumentException("Se requiere la ruta del Excel.");
        var result = await scope.ServiceProvider.GetRequiredService<WorkbookImporter>().ImportAsync(path);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result)); await SeedData.InitializeAsync(db); return;
    }
    await SeedData.InitializeAsync(db);
    foreach (var regionId in await db.Regions.Where(x => x.Id != Region.LegacyId).Select(x => x.Id).ToListAsync()) {
        db.RegionId = regionId; await SeedData.InitializeAsync(db);
    }
    db.RegionId = Region.LegacyId;
    if (args.Contains("--demo")) await DemoData.InitializeAsync(db, scope.ServiceProvider.GetRequiredService<BusinessRules>());
    foreach (var regionId in await db.Regions.Where(x => x.Status == "ACTIVO").Select(x => x.Id).ToListAsync()) {
        db.RegionId = regionId;
        await using var calendarTx = await db.Database.BeginTransactionAsync();
        await scope.ServiceProvider.GetRequiredService<LunchService>().ReconcileNationalCalendar();
        await calendarTx.CommitAsync();
    }
    db.RegionId = Region.LegacyId;
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
app.UseStaticFiles(); app.UseRouting(); app.UseAuthentication();
app.Use(async (context, next) => {
    var db = context.RequestServices.GetRequiredService<AppDbContext>();
    db.RegionId = context.User.FindFirstValue("region") ?? Region.LegacyId;
    if (context.User.IsInRole(Roles.GeneralAdmin) && context.Request.Cookies.TryGetValue("Almuerzo.Region", out var selected))
    {
        if (!await db.Regions.AnyAsync(x => x.Id == selected && x.Status == "ACTIVO")) {
            context.Response.Cookies.Delete("Almuerzo.Region");
            if (HttpMethods.IsGet(context.Request.Method)) context.Response.Redirect("/?admin=1"); else context.Response.StatusCode = 400;
            return;
        }
        db.RegionId = selected;
    }
    await next();
});
app.UseRateLimiter(); app.UseAuthorization();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
app.Run();
public partial class Program;
