using System.Net;
using Almuerzo.Web.Data;
using Almuerzo.Web.Models;
using Almuerzo.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Almuerzo.Tests;

public class DirectoryNamesTests
{
    private static AppFactory App(bool mapped = false)
    {
        var app = new AppFactory();
        app.ExtraConfiguration["Ad:OrganizationContract"] = "NamesV1";
        app.ExtraConfiguration["Ad:AutoProvisioningEnabled"] = "true";
        app.ExtraConfiguration["Ad:AutoCreateOrganizationEnabled"] = "true";
        if (mapped) {
            app.ExtraConfiguration["Ad:OrganizationMappings:0:Name"] = "Regional AD de prueba";
            app.ExtraConfiguration["Ad:OrganizationMappings:0:Id"] = "central";
            app.ExtraConfiguration["Ad:OrganizationMappings:0:Departments:0:Name"] = "Departamento AD de prueba";
            app.ExtraConfiguration["Ad:OrganizationMappings:0:Departments:0:Id"] = "a";
        }
        return app;
    }
    private static DirectoryIdentity Profile(string account = "new", string code = "1004", string region = "Regional AD de prueba", string department = "Departamento AD de prueba")
        => new(account + "@infotep.gob.do", "Persona sintética", code, DepartmentName: department, RegionName: region, JobTitle: "Puesto de prueba");
    private static async Task<string> Attempt(AppFactory app, HttpClient client, string email)
    {
        var html = await client.GetStringAsync("/Auth/Login");
        var response = await client.PostAsync("/Auth/Login", new FormUrlEncodedContent(new Dictionary<string,string> { ["Username"] = email, ["Password"] = "test-only", ["__RequestVerificationToken"] = AppFactory.Token(html) }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return await response.Content.ReadAsStringAsync();
    }
    [Fact] public async Task VerifiedNewNamesCreateCatalogAndSeedRegionalDefaultsIdempotently()
    {
        await using var app = App(); await app.Seed(); app.Profiles["new@infotep.gob.do"] = Profile();
        using var first = app.Client(); Assert.Equal("/", await app.Login(first, "new@infotep.gob.do"));
        var init = await app.Rpc(first, "apiGetInitData"); var regionId = init["regionalId"]!.ToString();
        Assert.NotEqual("central", regionId); Assert.Equal("USER", init["activeUser"]!["rol"]!.ToString()); Assert.Equal("Puesto de prueba", init["user"]!["cargo"]!.ToString());
        using var second = app.Client(); await app.Login(second, "new@infotep.gob.do");
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.RegionId = regionId;
        Assert.Equal(2, await db.Regions.CountAsync()); var department = Assert.Single(await db.Departments.ToListAsync());
        Assert.Empty(department.Admins); Assert.True(department.DirectoryManaged); Assert.Equal(64, department.DirectoryKey!.Length);
        Assert.Equal(9, await db.Categories.CountAsync()); Assert.Equal("57", (await db.Settings.FindAsync(regionId, "MEAL_PRICE_CURRENT"))!.Value);
        Assert.Equal(1, await db.Audit.CountAsync(x => x.Action == "DirectoryCreateRegion"));
    }
    [Fact] public async Task ExplicitLegacyMappingPreservesIdsContactOrdersAndRoles()
    {
        await using var app = App(true); await app.Seed(); app.Profiles["alias@infotep.gob.do"] = Profile("alias", "1000");
        using (var scope = app.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Orders.Add(new Order { Id = "historical", DepartmentId = "a", Email = "general@infotep.gob.do", Name = "Histórico sintético", Date = "2026-10-12" }); await db.SaveChangesAsync();
        }
        using var client = app.Client(); await app.Login(client, "alias@infotep.gob.do");
        var init = await app.Rpc(client, "apiGetInitData"); Assert.Equal("central", init["regionalId"]!.ToString()); Assert.Equal("ADMIN_GEN", init["activeUser"]!["rol"]!.ToString());
        using var inspect = app.Services.CreateScope(); var database = inspect.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("a", (await database.Users.SingleAsync(x => x.Email == "general@infotep.gob.do")).DepartmentId);
        Assert.Equal("a", (await database.Orders.SingleAsync(x => x.Id == "historical")).DepartmentId);
        Assert.Equal("Departamento AD de prueba", (await database.Departments.FindAsync("a"))!.Name);
        Assert.Single(await database.Regions.ToListAsync());
    }
    [Fact] public async Task UnmappedImportedAccountFailsWithoutCreatingOrMovingAnything()
    {
        await using var app = App(); await app.Seed(); app.Profiles["general@infotep.gob.do"] = Profile("general", "1000");
        using var client = app.Client(); Assert.Contains("mapear", await Attempt(app, client, "general@infotep.gob.do"));
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(await db.Regions.ToListAsync()); Assert.Equal(2, await db.Departments.CountAsync()); Assert.Equal("a", (await db.Users.FindAsync("general@infotep.gob.do"))!.DepartmentId);
    }
    [Fact] public async Task AliasesAreGeneralOnlyAndCannotModifyDirectoryIdentityOrScope()
    {
        await using var app = App(true); await app.Seed();
        app.Profiles["general@infotep.gob.do"] = Profile("general", "1000"); app.Profiles["admin@infotep.gob.do"] = Profile("admin", "1001");
        using var general = app.Client(); await app.Login(general, "general@infotep.gob.do");
        Assert.True((await app.Rpc(general, "apiSaveRegion", new { id = "central", nombre = "Regional AD de prueba", alias = "Región amigable", estado = "ACTIVO" }))["ok"]!.GetValue<bool>());
        Assert.True((await app.Rpc(general, "apiSaveDepartment", new { id = "a", nombre = "Departamento AD de prueba", alias = "Equipo amigable", admins = "admin@infotep.gob.do", estado = "ACTIVO" }))["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(general, "apiSaveRegion", new { id = "central", nombre = "Nombre manipulado", alias = "Otro", estado = "ACTIVO" }))["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(general, "apiSaveDepartment", new { id = "a", nombre = "Departamento AD de prueba", directoryKey = "forged" }))["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(general, "apiAdminSaveUser", new { email = "general@infotep.gob.do", cargo = "Cargo manipulado" }))["ok"]!.GetValue<bool>());
        using (var scope = app.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var user = await db.Users.FindAsync("admin@infotep.gob.do"); user!.Role = Roles.RegionalAdmin; await db.SaveChangesAsync(); }
        using var regional = app.Client(); await app.Login(regional, "admin@infotep.gob.do");
        Assert.False((await app.Rpc(regional, "apiSaveDepartment", new { id = "a", nombre = "Departamento AD de prueba", alias = "Ataque" }))["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(regional, "apiSaveRegion", new { id = "central", nombre = "Regional AD de prueba", alias = "Ataque" }))["ok"]!.GetValue<bool>());
        var init = await app.Rpc(general, "apiGetInitData"); Assert.Equal("Región amigable", init["regionalName"]!.ToString()); Assert.Equal("Equipo amigable", init["deptMap"]!["a"]!.ToString());
        using var inspect = app.Services.CreateScope(); var database = inspect.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("Regional AD de prueba", (await database.Regions.FindAsync("central"))!.Name);
        database.Orders.Add(new Order { Email = "general@infotep.gob.do", DepartmentId = "a", Name = "Histórico sintético", Date = "2026-10-12" }); await database.SaveChangesAsync();
        var report = await ReportLayout.Load(database, "2026-10-12", null); Assert.Equal("Región amigable", report.RegionName); Assert.Contains(report.Sections, x => x.Name == "Equipo amigable"); Assert.All(report.Sections.SelectMany(x => x.People), p => Assert.Equal("Equipo amigable", p.Department));
    }
    [Fact] public async Task AliasSurvivesNextLoginAndDoesNotBecomeADMatchingKey()
    {
        await using var app = App(); await app.Seed(); app.Profiles["new@infotep.gob.do"] = Profile();
        using var client = app.Client(); await app.Login(client, "new@infotep.gob.do");
        string id;
        using (var scope = app.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var region = await db.Regions.SingleAsync(x => x.DirectoryKey != null); id = region.Id;
            region.Alias = "Nombre público"; db.RegionId = id; (await db.Departments.SingleAsync()).Alias = "Equipo público"; await db.SaveChangesAsync();
        }
        app.Profiles["new@infotep.gob.do"] = Profile(region: " regional   ad de prueba ");
        using var again = app.Client(); await app.Login(again, "new@infotep.gob.do");
        var init = await app.Rpc(again, "apiGetInitData"); Assert.Equal(id, init["regionalId"]!.ToString()); Assert.Equal("Nombre público", init["regionalName"]!.ToString());
    }
    [Fact] public async Task InactiveAccountsAndDisabledAutoCreationCannotCreateCatalogs()
    {
        await using (var app = App()) {
            await app.Seed(); using (var scope = app.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); (await db.Users.FindAsync("user@infotep.gob.do"))!.Status = "INACTIVO"; await db.SaveChangesAsync(); }
            app.Profiles["user@infotep.gob.do"] = Profile("user", "1002"); using var client = app.Client(); Assert.Equal("/Auth/Denied", await app.Login(client, "user@infotep.gob.do"));
            using var inspect = app.Services.CreateScope(); Assert.Single(await inspect.ServiceProvider.GetRequiredService<AppDbContext>().Regions.ToListAsync());
        }
        await using (var app = App()) {
            app.ExtraConfiguration["Ad:AutoCreateOrganizationEnabled"] = "false"; await app.Seed(); app.Profiles["new@infotep.gob.do"] = Profile();
            using var client = app.Client(); Assert.Contains("habilitada", await Attempt(app, client, "new@infotep.gob.do"));
            using var inspect = app.Services.CreateScope(); Assert.False(await inspect.ServiceProvider.GetRequiredService<AppDbContext>().Users.AnyAsync(x => x.Email == "new@infotep.gob.do"));
        }
    }
    [Fact] public async Task FailedDepartmentMappingRollsBackRegionLinkAndUser()
    {
        await using var app = App(true); app.ExtraConfiguration["Ad:OrganizationMappings:0:Departments:0:Id"] = "missing"; await app.Seed(); app.Profiles["new@infotep.gob.do"] = Profile();
        using var client = app.Client(); Assert.Contains("mapeo", await Attempt(app, client, "new@infotep.gob.do"));
        using var inspect = app.Services.CreateScope(); var db = inspect.ServiceProvider.GetRequiredService<AppDbContext>(); Assert.Null((await db.Regions.FindAsync("central"))!.DirectoryKey); Assert.False(await db.Users.AnyAsync(x => x.Email == "new@infotep.gob.do"));
    }
    [Fact] public void NameKeyPreservesAccentAndPunctuationAndIgnoresCaseWhitespaceAndUnicodeComposition()
    {
        Assert.Equal(DirectoryCatalog.Key(" Región  Central "), DirectoryCatalog.Key("REGIÓN CENTRAL"));
        Assert.NotEqual(DirectoryCatalog.Key("Región Central"), DirectoryCatalog.Key("Region Central"));
        Assert.NotEqual(DirectoryCatalog.Key("252- Equipo"), DirectoryCatalog.Key("252 Equipo"));
    }
    [Fact] public async Task NamesTransferDemotesRegionalAdminAndPreservesHistoricalOrders()
    {
        await using var app = App(); await app.Seed(); app.Profiles["new@infotep.gob.do"] = Profile();
        using var client = app.Client(); await app.Login(client, "new@infotep.gob.do"); string oldRegion;
        using (var scope = app.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var user = await db.Users.IgnoreQueryFilters().SingleAsync(x => x.Email == "new@infotep.gob.do");
            oldRegion = user.RegionId; db.RegionId = oldRegion; user.Role = Roles.RegionalAdmin; user.RegionalBlocked = true;
            db.Orders.Add(new Order { Id = "old-context", DepartmentId = user.DepartmentId!, Email = user.Email, Name = user.Name, Date = "2026-10-12" }); await db.SaveChangesAsync();
        }
        app.Profiles["new@infotep.gob.do"] = Profile(region: "Otra regional sintética");
        using var again = app.Client(); await app.Login(again, "new@infotep.gob.do"); var init = await app.Rpc(again, "apiGetInitData");
        Assert.NotEqual(oldRegion, init["regionalId"]!.ToString()); Assert.Equal("USER", init["activeUser"]!["rol"]!.ToString()); Assert.False(init["activeUser"]!["regionalBlocked"]!.GetValue<bool>());
        using var inspect = app.Services.CreateScope(); var database = inspect.ServiceProvider.GetRequiredService<AppDbContext>(); Assert.Equal(oldRegion, (await database.Orders.IgnoreQueryFilters().SingleAsync(x => x.Id == "old-context")).RegionId);
    }
    [Fact] public async Task InactiveOrganizationCannotBeRecreatedOrReactivatedByAD()
    {
        await using var app = App(); await app.Seed(); app.Profiles["new@infotep.gob.do"] = Profile();
        using (var scope = app.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.Regions.Add(new Region { Id = "inactive", Name = "Regional AD de prueba", DirectoryKey = DirectoryCatalog.Key("Regional AD de prueba"), DirectoryManaged = true, Status = "INACTIVO" }); await db.SaveChangesAsync(); }
        using var client = app.Client(); Assert.Contains("inactiva", await Attempt(app, client, "new@infotep.gob.do"));
        using var inspect = app.Services.CreateScope(); var database = inspect.ServiceProvider.GetRequiredService<AppDbContext>(); Assert.Equal(2, await database.Regions.CountAsync()); Assert.False(await database.Users.IgnoreQueryFilters().AnyAsync(x => x.Email == "new@infotep.gob.do"));
    }
}
