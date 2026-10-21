using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Almuerzo.Web.Data;
using Almuerzo.Web.Models;
using Almuerzo.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Almuerzo.Tests;

public class RegionalTests
{
    private static async Task National(AppFactory app)
    {
        await app.Seed();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AllowCrossRegionWrites = true;
        db.Regions.Add(new Region { Id = "east", Name = "Regional de prueba Este", DirectoryCode = "E" });
        db.Departments.Add(new Department { Id = "east-a", RegionId = "east", Name = "Departamento A", DirectoryCode = "D1" });
        foreach (var (email, code, role, region, dept) in new[] {
            ("regional@infotep.gob.do", "1005", Roles.RegionalAdmin, "east", "east-a"),
            ("east-user@infotep.gob.do", "1006", Roles.User, "east", "east-a"),
            ("east-admin@infotep.gob.do", "1007", Roles.DepartmentAdmin, "east", "east-a"),
            ("central-reg@infotep.gob.do", "1008", Roles.RegionalAdmin, "central", "a") })
        {
            db.Users.Add(new AppUser { Email = email, Name = "Perfil sintético", EmployeeCode = code, RegionId = region, DepartmentId = dept, Role = role, Status = "ACTIVO" });
            app.Profiles[email] = new(email, "Perfil sintético", code);
        }
        await db.SaveChangesAsync(); db.RegionId = "east"; await SeedData.InitializeAsync(db);
        db.Menu.Add(new MenuItem { Id = "east-food", Date = "2026-10-12", CategoryId = "east:Arroces", Dish = "Arroz de la regional Este" });
        (await db.Settings.FindAsync("east", "PROVIDER_NAME"))!.Value = "Proveedor Este";
        (await db.Settings.FindAsync("east", "MEAL_PRICE_CURRENT"))!.Value = "80";
        await db.SaveChangesAsync();
    }
    [Fact] public async Task RegionalAdminSeesOnlyOwnRegionAndCannotEscalateOrChangePrices()
    {
        await using var app = new AppFactory(); await National(app); using var c = app.Client(); await app.Login(c, "regional@infotep.gob.do");
        var init = await app.Rpc(c, "apiGetInitData"); Assert.Equal("east", init["regionalId"]!.ToString()); Assert.Contains("Proveedor Este", init.ToJsonString()); Assert.DoesNotContain("\"id\":\"rice\"", init["menu"]!.ToJsonString());
        Assert.Single(init["regions"]!.AsArray());
        var users = (await app.Rpc(c, "apiGetAdminData")).ToJsonString(); Assert.Contains("east-user@", users); Assert.DoesNotContain("user@infotep.gob.do\",\"nombre\":\"Usuario A", users);
        Assert.False((await app.Rpc(c, "apiSaveConfig", new Dictionary<string,string> { ["MEAL_PRICE_CURRENT"] = "5" }))["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(c, "apiResetProviderPeriod", new { providerName = "Intruso", price = 5 }))["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(c, "apiAdminSaveUser", new { email = "east-user@infotep.gob.do", codigo = "1006", nombre = "Este", departamento = "east-a", rol = "ADMIN_GEN", estado = "ACTIVO" }))["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(c, "apiAdminSaveUser", new { email = "east-user@infotep.gob.do", codigo = "1006", nombre = "Este", departamento = "east-a", rol = "ADMIN_DEP", estado = "ACTIVO" }))["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(c, "apiSaveConfig", new Dictionary<string,string> { ["HORA_ENVIO"] = "14:00" }))["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(c, "apiSaveConfig", new Dictionary<string,string> { ["MINUTOS_PREV_CIERRE"] = "30.0" }))["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(c, "apiDeleteMenuItem", "rice"))["ok"]!.GetValue<bool>());
    }
    [Fact] public async Task OnlyGeneralAdminCanSelectAnotherRegionAndProviderRequiresExplicitPrice()
    {
        await using var app = new AppFactory(); await National(app); using var general = app.Client(); using var regional = app.Client();
        await app.Login(general, "general@infotep.gob.do"); await app.Login(regional, "regional@infotep.gob.do");
        var token = AppFactory.RpcToken(await regional.GetStringAsync("/"));
        Assert.Equal(HttpStatusCode.Forbidden, (await regional.PostAsync("/regions/select", new FormUrlEncodedContent(new Dictionary<string,string> { ["regionId"] = "central", ["__RequestVerificationToken"] = token }))).StatusCode);
        token = AppFactory.RpcToken(await general.GetStringAsync("/"));
        var response = await general.PostAsync("/regions/select", new FormUrlEncodedContent(new Dictionary<string,string> { ["regionId"] = "east", ["__RequestVerificationToken"] = token })); Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("east", (await app.Rpc(general, "apiGetInitData"))["regionalId"]!.ToString());
        Assert.False((await app.Rpc(general, "apiSubmitProviderRating", new { rating = 5, comment = "Fuera del ámbito personal" }))["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(general, "apiResetProviderPeriod", new { providerName = "Nuevo" }))["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(general, "apiSaveConfig", new Dictionary<string,string> { ["PROVIDER_NAME"] = "Atajo" }))["ok"]!.GetValue<bool>());
        Assert.True((await app.Rpc(general, "apiResetProviderPeriod", new { providerName = "Nuevo Este", price = 85.50m }))["ok"]!.GetValue<bool>());
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.RegionId = "east";
        Assert.Equal("85.50", (await db.Settings.FindAsync("east", "MEAL_PRICE_CURRENT"))!.Value);
        Assert.Contains("85.50", (await db.Settings.FindAsync("east", "MEAL_PRICE_HISTORY_JSON"))!.Value);
        db.RegionId = "central"; Assert.Equal("Proveedor de Alimentos", (await db.Settings.FindAsync("central", "PROVIDER_NAME"))!.Value);
    }
    [Fact] public async Task ForgedRegionalCookieAndForeignIdsDoNotCrossScope()
    {
        await using var app = new AppFactory(); await National(app); using var c = app.Client(); await app.Login(c, "regional@infotep.gob.do");
        c.DefaultRequestHeaders.Add("Cookie", "Almuerzo.Region=central");
        Assert.Equal("east", (await app.Rpc(c, "apiGetInitData"))["regionalId"]!.ToString());
        Assert.False((await app.Rpc(c, "apiGetInitData", "2026-10-12", "user@infotep.gob.do"))["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(c, "apiAdminDeleteUser", "general@infotep.gob.do"))["ok"]!.GetValue<bool>());
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.RegionId = "east"; var foreign = await db.Menu.IgnoreQueryFilters().SingleAsync(x => x.Id == "rice"); foreign.Dish = "Intento";
        await Assert.ThrowsAsync<BusinessException>(() => db.SaveChangesAsync());
    }
    [Fact] public async Task VerifiedOrganizationProvisionsUserAndKeepsInactiveAccountsBlocked()
    {
        await using var app = new AppFactory(); app.ExtraConfiguration["Ad:AutoProvisioningEnabled"] = "true"; await National(app);
        app.Profiles["new@infotep.gob.do"] = new("new@infotep.gob.do", "Persona de prueba", "1004", "E", "D1", "Departamento A");
        using var c = app.Client(); Assert.Equal("/", await app.Login(c, "new@infotep.gob.do"));
        var init = await app.Rpc(c, "apiGetInitData"); Assert.Equal("east", init["regionalId"]!.ToString()); Assert.Equal("USER", init["activeUser"]!["rol"]!.ToString());
        using (var scope = app.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.RegionId = "east"; var user = await db.Users.SingleAsync(x => x.Email == "new@infotep.gob.do"); user.Status = "INACTIVO"; await db.SaveChangesAsync(); }
        using var blocked = app.Client(); Assert.Equal("/Auth/Denied", await app.Login(blocked, "new@infotep.gob.do"));
    }
    [Fact] public async Task UnmappedDirectoryRegionDoesNotCreatePrivilegedOrEnabledAccounts()
    {
        await using var app = new AppFactory(); app.ExtraConfiguration["Ad:AutoProvisioningEnabled"] = "true"; await National(app);
        app.Profiles["new@infotep.gob.do"] = new("new@infotep.gob.do", "Persona", "1004", "UNKNOWN", "D1", "Departamento A");
        using var c = app.Client(); var html = await c.GetStringAsync("/Auth/Login");
        var response = await c.PostAsync("/Auth/Login", new FormUrlEncodedContent(new Dictionary<string,string> { ["Username"] = "new@infotep.gob.do", ["Password"] = "test-only", ["__RequestVerificationToken"] = AppFactory.Token(html) }));
        Assert.Contains("configurada", await response.Content.ReadAsStringAsync());
        using var scope = app.Services.CreateScope(); Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.IgnoreQueryFilters().AnyAsync(x => x.Email == "new@infotep.gob.do"));
    }
    [Fact] public async Task DirectoryDepartmentMoveRevokesDepartmentAdminWithoutCreatingAResponsible()
    {
        await using var app = new AppFactory(); app.ExtraConfiguration["Ad:AutoProvisioningEnabled"] = "true"; await National(app);
        app.Profiles["east-admin@infotep.gob.do"] = new("east-admin@infotep.gob.do", "Persona de prueba", "1007", "E", "D2", "Nuevo departamento AD");
        using var c = app.Client(); await app.Login(c, "east-admin@infotep.gob.do");
        var init = await app.Rpc(c, "apiGetInitData"); Assert.Equal("USER", init["activeUser"]!["rol"]!.ToString());
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.RegionId = "east";
        var dept = await db.Departments.SingleAsync(x => x.DirectoryCode == "D2"); Assert.Empty(dept.Admins); Assert.Equal("ACTIVO", dept.Status);
    }
    [Fact] public async Task DirectoryProvisioningDoesNotActivateAPendingAdministrativeRole()
    {
        await using var app = new AppFactory(); app.ExtraConfiguration["Ad:AutoProvisioningEnabled"] = "true"; await National(app);
        using (var scope = app.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.RegionId = "east";
            (await db.Users.SingleAsync(x => x.Email == "regional@infotep.gob.do")).Status = "PENDIENTE"; await db.SaveChangesAsync();
        }
        app.Profiles["regional@infotep.gob.do"] = new("regional@infotep.gob.do", "Persona", "1005", "E", "D1", "Departamento A");
        using var c = app.Client(); Assert.Equal("/Auth/Denied", await app.Login(c, "regional@infotep.gob.do"));
    }
    [Fact] public async Task OpenSessionChargesOnlyPlayedTimeAndFinalRetryIsIdempotent()
    {
        await using var app = new AppFactory(); await app.Seed(); using var c = app.Client(); await app.Login(c, "user@infotep.gob.do");
        var start = await app.Rpc(c, "apiStartLunchRush", "pot-v5"); Assert.True(start["ok"]!.GetValue<bool>()); Assert.Equal(900, start["duration"]!.GetValue<int>()); Assert.Equal(900, start["state"]!["remainingSeconds"]!.GetValue<int>());
        Assert.False((await app.Rpc(c, "apiStartLunchRush", "pot-v5"))["ok"]!.GetValue<bool>());
        app.Clock.Current = app.Clock.Current.AddSeconds(7);
        var finish = await app.Rpc(c, "apiFinishLunchRush", new { id = start["id"]!.ToString(), moves = new[] { new { t = 100, lane = 2 } }, score = 999999 });
        Assert.True(finish["ok"]!.GetValue<bool>()); Assert.True(finish["state"]!["score"]!.GetValue<int>() < 999999); Assert.Equal(893, finish["state"]!["remainingSeconds"]!.GetValue<int>());
        var retry = await app.Rpc(c, "apiFinishLunchRush", new { id = start["id"]!.ToString(), moves = Array.Empty<object>() });
        Assert.True(retry["ok"]!.GetValue<bool>()); Assert.Equal(finish["state"]!.ToJsonString(), retry["state"]!.ToJsonString());
        Assert.Equal(893, (await app.Rpc(c, "apiStartLunchRush", "pot-v5"))["duration"]!.GetValue<int>());
    }
    [Fact] public async Task HistoricalReportKeepsEmployeeCodeAfterRegionalTransfer()
    {
        await using var app = new AppFactory(); await National(app);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Orders.Add(new Order { Email = "user@infotep.gob.do", Name = "Histórico", DepartmentId = "a", Date = "2026-10-12", Status = "ACTIVO", Summary = "Arroz Blanco" });
        var user = await db.Users.SingleAsync(x => x.Email == "user@infotep.gob.do"); db.AllowCrossRegionWrites = true; user.RegionId = "east"; user.DepartmentId = "east-a";
        await db.SaveChangesAsync();
        var report = await ReportLayout.Load(db, "2026-10-12", null); Assert.Equal("1002", Assert.Single(report.Sections[0].People).Code);
    }
    [Fact] public async Task InactiveRegionShowsDeniedWithoutRedirectLoop()
    {
        await using var app = new AppFactory(); await National(app);
        using (var scope = app.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); (await db.Regions.FindAsync("east"))!.Status = "INACTIVO"; await db.SaveChangesAsync();
        }
        using var c = app.Client(); Assert.Equal("/Auth/Denied", await app.Login(c, "east-user@infotep.gob.do"));
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/Auth/Denied")).StatusCode);
    }
    [Fact] public async Task RushDailyLimitCannotBeBypassedByAbandoningRoundsOrRelogging()
    {
        await using var app = new AppFactory(); await app.Seed(); using var c = app.Client(); await app.Login(c, "user@infotep.gob.do");
        for (var i = 0; i < 45; i++) {
            Assert.True((await app.Rpc(c, "apiStartLunchRush", "pot-v5"))["ok"]!.GetValue<bool>());
            app.Clock.Current = app.Clock.Current.AddSeconds(21);
        }
        using var again = app.Client(); await app.Login(again, "user@infotep.gob.do");
        Assert.False((await app.Rpc(again, "apiStartLunchRush", "pot-v5"))["ok"]!.GetValue<bool>());
        Assert.Equal(0, (await app.Rpc(again, "apiGetInitData"))["chefGame"]!["remainingSeconds"]!.GetValue<int>());
        app.Clock.Current = app.Clock.Current.AddDays(1);
        using var nextDay = app.Client(); await app.Login(nextDay, "user@infotep.gob.do");
        Assert.True((await app.Rpc(nextDay, "apiStartLunchRush", "pot-v5"))["ok"]!.GetValue<bool>());
    }
    [Fact] public async Task RegionalMenuRetainsWhiteRiceAndGrainRules()
    {
        await using var app = new AppFactory(); await National(app);
        using (var scope = app.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.RegionId = "east";
            db.Menu.AddRange(new MenuItem { Id = "grain", Date = "2026-10-12", CategoryId = "east:Granos", Dish = "Habichuelas" }, new MenuItem { Id = "white", Date = "2026-10-12", CategoryId = "east:Arroces", Dish = "Arroz Blanco" });
            await db.SaveChangesAsync();
        }
        using var c = app.Client(); await app.Login(c, "east-user@infotep.gob.do");
        Assert.False((await app.Rpc(c, "apiSubmitOrder", new { date = "2026-10-12", categorias = new[] { "east:Granos" }, items = new[] { "Habichuelas" } }))["ok"]!.GetValue<bool>());
        Assert.True((await app.Rpc(c, "apiSubmitOrder", new { date = "2026-10-12", categorias = new[] { "east:Arroces", "east:Granos" }, items = new[] { "Arroz Blanco", "Habichuelas" } }))["ok"]!.GetValue<bool>());
    }
}
