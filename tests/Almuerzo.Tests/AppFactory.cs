using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Almuerzo.Web.Data;
using Almuerzo.Web.Models;
using Almuerzo.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Almuerzo.Tests;

public class FixedClock : TimeProvider
{
    public DateTimeOffset Current = new(2026, 10, 9, 16, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Current;
}
public class TestDirectory(Dictionary<string, DirectoryIdentity> profiles) : IDirectoryIdentityProvider
{
    public Task<DirectoryIdentity?> AuthenticateAsync(string username, string password, CancellationToken ct)
        => Task.FromResult<DirectoryIdentity?>(password == "test-only" && username.EndsWith("@infotep.gob.do") ? profiles.GetValueOrDefault(username) ?? new DirectoryIdentity(username, "Colaborador de prueba", username.Split('@')[0] switch { "general" or "alias" => "1000", "admin" => "1001", "user" => "1002", "other" => "1003", _ => "1004" }) : null);
}
public class AppFactory : WebApplicationFactory<Program>
{
    public readonly FixedClock Clock = new();
    public readonly Dictionary<string, DirectoryIdentity> Profiles = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, string?> ExtraConfiguration = new() { ["Ad:OrganizationContract"] = "Codes", ["Ad:AutoProvisioningEnabled"] = "false", ["Ad:AutoCreateOrganizationEnabled"] = "false" };
    public readonly string SqlDatabase = "AlmuerzoTest_" + Guid.NewGuid().ToString("N");
    public bool UseSqlServer => Environment.GetEnvironmentVariable("ALMUERZO_TEST_SQLSERVER") == "1";
    public readonly string Storage = Path.Combine(Path.GetTempPath(), "almuerzo-tests", Guid.NewGuid().ToString("N"));
    private bool configured;
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        configured = true;
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Provider"] = "Sqlite", ["ConnectionStrings:DefaultConnection"] = "Data Source=" + Path.Combine(Storage, "almuerzo.db"), ["Storage:Path"] = Storage, ["Operations:Enabled"] = "false", ["Logging:LogLevel:Default"] = "Warning", ["AllowedHosts"] = "localhost" }));
        builder.ConfigureAppConfiguration((_, c) => { if (UseSqlServer) c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Provider"] = "SqlServer", ["ConnectionStrings:DefaultConnection"] = $"Server=(localdb)\\MSSQLLocalDB;Database={SqlDatabase};Integrated Security=true;Encrypt=true;TrustServerCertificate=false" }); c.AddInMemoryCollection(ExtraConfiguration); });
        builder.ConfigureServices(s => { s.RemoveAll<IDirectoryIdentityProvider>(); s.AddSingleton<IDirectoryIdentityProvider>(new TestDirectory(Profiles)); s.RemoveAll<TimeProvider>(); s.AddSingleton<TimeProvider>(Clock); });
    }
    public async Task Seed()
    {
        using var scope = Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Departments.AddRange(new Department { Id = "a", Name = "Departamento A" }, new Department { Id = "b", Name = "Departamento B" });
        db.Users.AddRange(
            new AppUser { Email = "general@infotep.gob.do", Name = "General", DepartmentId = "a", EmployeeCode = "1000", Role = Roles.GeneralAdmin, Status = "ACTIVO" },
            new AppUser { Email = "admin@infotep.gob.do", Name = "Admin A", DepartmentId = "a", EmployeeCode = "1001", Role = Roles.DepartmentAdmin, Status = "ACTIVO" },
            new AppUser { Email = "user@infotep.gob.do", Name = "Usuario A", DepartmentId = "a", EmployeeCode = "1002", Role = Roles.User, Status = "ACTIVO" },
            new AppUser { Email = "other@infotep.gob.do", Name = "Usuario B", DepartmentId = "b", EmployeeCode = "1003", Role = Roles.User, Status = "ACTIVO" });
        db.Menu.AddRange(new MenuItem { Id = "rice", Date = "2026-10-12", CategoryId = "Arroces", Dish = "Arroz Blanco" }, new MenuItem { Id = "meat", Date = "2026-10-12", CategoryId = "Carnes", Dish = "Pollo" });
        await db.SaveChangesAsync();
    }
    public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    public static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    public static string RpcToken(string html) => Regex.Match(html, "name=\"csrf-token\" content=\"([^\"]+)\"").Groups[1].Value;
    public async Task<string> Login(HttpClient client, string email)
    {
        var html = await client.GetStringAsync("/Auth/Login"); Assert.NotEmpty(Token(html));
        var response = await client.PostAsync("/Auth/Login", new FormUrlEncodedContent(new Dictionary<string, string> { ["Username"] = email, ["Password"] = "test-only", ["__RequestVerificationToken"] = Token(html) }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response.Headers.Location!.ToString();
    }
    public async Task<JsonObject> Rpc(HttpClient client, string method, params object[] args)
    {
        var html = await client.GetStringAsync("/"); var token = RpcToken(html); Assert.NotEmpty(token);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/rpc") { Content = JsonContent.Create(new { method, args }) };
        request.Headers.Add("X-CSRF-TOKEN", token); var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (configured && UseSqlServer && SqlDatabase.StartsWith("AlmuerzoTest_", StringComparison.Ordinal) && Guid.TryParseExact(SqlDatabase[13..], "N", out _)) {
            var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<SqlServerAppDbContext>().UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={SqlDatabase};Integrated Security=true;Encrypt=true;TrustServerCertificate=false").Options;
            await using var db = new SqlServerAppDbContext(options); await db.Database.EnsureDeletedAsync();
        }
    }
}
