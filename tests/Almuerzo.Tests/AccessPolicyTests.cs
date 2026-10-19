using System.Net;
using System.Text.Json.Nodes;
using Almuerzo.Web.Data;
using Almuerzo.Web.Models;
using Almuerzo.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Almuerzo.Tests;

public class AccessPolicyTests
{
    private static async Task<AppFactory> Create(bool directoryOrganization = false)
    {
        var app = new AppFactory(); app.ExtraConfiguration["Ad:AutoProvisioningEnabled"] = directoryOrganization.ToString(); await app.Seed();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.AllowCrossRegionWrites = true;
        (await db.Regions.FindAsync("central"))!.DirectoryCode = "C";
        (await db.Departments.FindAsync("a"))!.DirectoryCode = "D1";
        db.Regions.Add(new Region { Id = "east", Name = "Este de prueba", DirectoryCode = "E" });
        db.Departments.Add(new Department { Id = "east-a", RegionId = "east", Name = "Departamento Este", DirectoryCode = "DE" });
        db.Users.AddRange(new AppUser { Email = "regional@infotep.gob.do", Name = "Regional", EmployeeCode = "1005", Role = Roles.RegionalAdmin, Status = "ACTIVO", DepartmentId = "a" },
            new AppUser { Email = "foreign@infotep.gob.do", Name = "Otra regional", EmployeeCode = "1006", Role = Roles.User, Status = "ACTIVO", RegionId = "east", DepartmentId = "east-a" });
        await db.SaveChangesAsync();
        db.RegionId = "east"; await SeedData.InitializeAsync(db); db.RegionId = "central";
        foreach (var (name, code) in new[] { ("general","1000"), ("admin","1001"), ("user","1002"), ("regional","1005") })
            app.Profiles[name + "@infotep.gob.do"] = new(name + "@infotep.gob.do", "Nombre verificado " + name, code, "C", "D1", "Departamento A");
        return app;
    }

    [Fact] public async Task RegionalAdminCannotChangeIdentityOrganizationRolesOrInstitutionalSettings()
    {
        await using var app = await Create(); using var regional = app.Client(); await app.Login(regional, "regional@infotep.gob.do");
        foreach (var (method, json) in new[] {
            ("apiAdminSaveUser", """{"email":"user@infotep.gob.do","nombre":"Intruso","codigo":"1002","departamento":"a","rol":"USER","estado":"ACTIVO"}"""),
            ("apiAdminSaveUser", """{"email":"user@infotep.gob.do","rol":"ADMIN_DEP"}"""),
            ("apiAdminSetUserStatus", """{"email":"user@infotep.gob.do","estado":"ACTIVO"}"""),
            ("apiSaveDepartment", """{"id":"a","nombre":"Alterado","admins":"user@infotep.gob.do"}"""),
            ("apiSaveMenuCategory", """{"id":"Arroces","nombre":"Otra regla"}"""),
            ("apiSaveConfig", """{"APP_URL":"https://example.test"}"""),
            ("apiSaveConfig", """{"HORA_ENVIO":"23:59","MINUTOS_PREV_CIERRE":"0"}"""),
            ("apiSaveConfig", """{"TEST_EMAIL_MODE":"FALSE","ADMIN_EMAILS":"exfil@example.test"}"""),
            ("apiSendDailyCloseEmailsTest", "{}") })
            Assert.False((await app.Rpc(regional, method, JsonNode.Parse(json)!))["ok"]!.GetValue<bool>(), method);
        Assert.False((await app.Rpc(regional, "apiDeleteDepartment", "b"))["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(regional, "apiAdminDeleteUser", "user@infotep.gob.do"))["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(regional, "apiResetDirectoryLink", "user@infotep.gob.do"))["ok"]!.GetValue<bool>());
        var data = await app.Rpc(regional, "apiGetAdminData"); Assert.Empty(data["config"]!.AsObject()); Assert.Empty(data["configList"]!.AsArray());
        var token = AppFactory.RpcToken(await regional.GetStringAsync("/"));
        Assert.Equal(HttpStatusCode.Forbidden, (await regional.PostAsync("/branding", new FormUrlEncodedContent(new Dictionary<string,string> { ["footerText"]="Alterado",["__RequestVerificationToken"]=token }))).StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.FindAsync("user@infotep.gob.do"); Assert.Equal("Usuario A", user!.Name); Assert.Equal("1002", user.EmployeeCode); Assert.Equal(Roles.User, user.Role); Assert.Equal("a", user.DepartmentId);
    }

    [Fact] public async Task RegionalAdminRetainsMenuCalendarAnnouncementsAndOrderOperations()
    {
        await using var app = await Create(); using var regional = app.Client(); using var user = app.Client(); await app.Login(regional,"regional@infotep.gob.do"); await app.Login(user,"user@infotep.gob.do");
        var order = await app.Rpc(user,"apiSubmitOrder",new { date="2026-10-12", categorias=new[]{"Arroces"},items=new[]{"Arroz Blanco"} });
        Assert.True((await app.Rpc(regional,"apiAdminCancelOrder",order["order"]!["id"]!.ToString()))["ok"]!.GetValue<bool>());
        Assert.True((await app.Rpc(regional,"apiSaveWeeklyMenu",new Dictionary<string,object> { ["2026-10-13"]=new[]{new { cat="Arroces",plato="Arroz de prueba",desc=""}}}))["ok"]!.GetValue<bool>());
        Assert.True((await app.Rpc(regional,"apiSaveHoliday","2026-10-14","Cierre local"))["ok"]!.GetValue<bool>());
        Assert.True((await app.Rpc(regional,"apiSaveAnnouncementConfig",new {enabled=true,slides=new[]{new {title="Aviso local",description="Mensaje",icon="fa-star"}}}))["ok"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.OK,(await regional.GetAsync("/reports/daily/2026-10-12")).StatusCode);
    }

    [Fact] public async Task RegionalBlockRevokesSessionsSurvivesDirectoryLoginAndCannotReactivateGeneralDisable()
    {
        await using var app = await Create(true); using var regional=app.Client(); using var user=app.Client(); using var general=app.Client();
        await app.Login(regional,"regional@infotep.gob.do"); await app.Login(user,"user@infotep.gob.do"); await app.Login(general,"general@infotep.gob.do");
        Assert.True((await app.Rpc(regional,"apiSetRegionalUserBlock",new {email="user@infotep.gob.do",blocked=true}))["ok"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.Redirect,(await user.GetAsync("/")).StatusCode);
        using var again=app.Client(); Assert.Equal("/Auth/Denied",await app.Login(again,"user@infotep.gob.do"));
        var denied=await again.GetStringAsync("/Auth/Denied"); Assert.DoesNotContain("Solicitar acceso",denied);
        await again.PostAsync("/Auth/RequestAccess",new FormUrlEncodedContent(new Dictionary<string,string> {["__RequestVerificationToken"]=AppFactory.Token(denied)}));
        Assert.True((await app.Rpc(general,"apiAdminSetUserStatus",new {email="user@infotep.gob.do",estado="INACTIVO"}))["ok"]!.GetValue<bool>());
        Assert.True((await app.Rpc(regional,"apiSetRegionalUserBlock",new {email="user@infotep.gob.do",blocked=false}))["ok"]!.GetValue<bool>());
        using var stillBlocked=app.Client(); Assert.Equal("/Auth/Denied",await app.Login(stillBlocked,"user@infotep.gob.do"));
        using var scope=app.Services.CreateScope(); var db=scope.ServiceProvider.GetRequiredService<AppDbContext>(); var stored=await db.Users.FindAsync("user@infotep.gob.do");
        Assert.Equal("INACTIVO",stored!.Status); Assert.False(stored.RegionalBlocked); Assert.Equal(Roles.User,stored.Role); Assert.Equal("1002",stored.EmployeeCode);
        Assert.Equal(2,await db.Audit.CountAsync(x=>x.Action=="RegionalAccessBlocked"||x.Action=="RegionalAccessRestored"));
    }

    [Fact] public async Task RegionalBlockCannotAffectPeersOtherRegionsOrCarryExtraEditableFields()
    {
        await using var app=await Create(); using var regional=app.Client(); await app.Login(regional,"regional@infotep.gob.do");
        foreach (var email in new[]{"general@infotep.gob.do","regional@infotep.gob.do","foreign@infotep.gob.do"})
            Assert.False((await app.Rpc(regional,"apiSetRegionalUserBlock",new {email,blocked=true}))["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(regional,"apiSetRegionalUserBlock",new {email="user@infotep.gob.do",blocked=true,rol="ADMIN_GEN"}))["ok"]!.GetValue<bool>());
        using var dept=app.Client(); await app.Login(dept,"admin@infotep.gob.do");
        Assert.False((await app.Rpc(dept,"apiSetRegionalUserBlock",new {email="user@infotep.gob.do",blocked=true}))["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(dept,"apiAdminDeleteUser","user@infotep.gob.do"))["ok"]!.GetValue<bool>());
    }

    [Fact] public async Task GeneralAdminManagesLocalRolesButCannotRewriteIdentityOrCreateManualIdentities()
    {
        await using var app=await Create(); using var general=app.Client(); await app.Login(general,"general@infotep.gob.do");
        foreach (var json in new[]{"""{"email":"user@infotep.gob.do","nombre":"Otro"}""","""{"email":"user@infotep.gob.do","codigo":"9999"}""","""{"email":"user@infotep.gob.do","regionalId":"east"}""","""{"email":"user@infotep.gob.do","directoryAccount":"otro@infotep.gob.do"}""","""{"email":"user@infotep.gob.do","directoryManaged":true}"""})
            Assert.False((await app.Rpc(general,"apiAdminSaveUser",JsonNode.Parse(json)!))["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(general,"apiAdminSaveUser",new {email="inventado@infotep.gob.do",nombre="Inventado",codigo="2222",departamento="a",rol="USER",estado="ACTIVO"}))["ok"]!.GetValue<bool>());
        Assert.True((await app.Rpc(general,"apiAdminSaveUser",new {email="user@infotep.gob.do",rol="ADMIN_DEP",departamento="a",estado="ACTIVO"}))["ok"]!.GetValue<bool>());
        using var scope=app.Services.CreateScope(); var db=scope.ServiceProvider.GetRequiredService<AppDbContext>(); var stored=await db.Users.FindAsync("user@infotep.gob.do"); Assert.Equal("Usuario A",stored!.Name); Assert.Equal("1002",stored.EmployeeCode); Assert.Equal(Roles.DepartmentAdmin,stored.Role);
    }

    [Fact] public async Task VerifiedDepartmentsAreImmutableAndResponsibleAssignmentNeverTransfersUsers()
    {
        await using var app=await Create(true); using var user=app.Client(); using var general=app.Client(); await app.Login(user,"user@infotep.gob.do"); await app.Login(general,"general@infotep.gob.do");
        Assert.False((await app.Rpc(general,"apiAdminSaveUser",new {email="user@infotep.gob.do",departamento="b"}))["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(general,"apiSaveDepartment",new {id="a",nombre="Inventado",directoryCode="D1",admins=""}))["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(general,"apiSaveDepartment",new {id="a",nombre="Departamento A",directoryCode="OTRO",admins=""}))["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(general,"apiSaveDepartment",new {id="a",nombre="Departamento A",directoryCode="D1",admins="other@infotep.gob.do"}))["ok"]!.GetValue<bool>());
        Assert.True((await app.Rpc(general,"apiSaveDepartment",new {id="a",nombre="Departamento A",directoryCode="D1",admins="user@infotep.gob.do"}))["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(general,"apiSaveRegion",new {id="central",nombre="Sede inicial",directoryCode="ALTERADO",estado="ACTIVO"}))["ok"]!.GetValue<bool>());
        using var scope=app.Services.CreateScope(); var db=scope.ServiceProvider.GetRequiredService<AppDbContext>(); Assert.Equal("b",(await db.Users.FindAsync("other@infotep.gob.do"))!.DepartmentId);
    }

    [Fact] public async Task BlockedDepartmentResponsibleKeepsOrdersButDoesNotReceiveSummary()
    {
        await using var app=await Create(); using var regional=app.Client(); await app.Login(regional,"regional@infotep.gob.do");
        using (var s=app.Services.CreateScope()) { var db=s.ServiceProvider.GetRequiredService<AppDbContext>(); db.Orders.Add(new Order {Email="admin@infotep.gob.do",Name="Pedido previo",DepartmentId="a",Date="2026-10-12",Status="ACTIVO",Summary="Arroz"}); await db.SaveChangesAsync(); }
        Assert.True((await app.Rpc(regional,"apiSetRegionalUserBlock",new {email="admin@infotep.gob.do",blocked=true}))["ok"]!.GetValue<bool>());
        using var scope=app.Services.CreateScope(); var dbCheck=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("ACTIVO",(await dbCheck.Orders.SingleAsync()).Status);
        await scope.ServiceProvider.GetRequiredService<ReportService>().EnqueueCloseAsync("2026-10-12"); await dbCheck.SaveChangesAsync();
        Assert.False(await dbCheck.Outbox.AnyAsync(x=>x.Recipient=="admin@infotep.gob.do")); Assert.True(await dbCheck.Outbox.AnyAsync(x=>x.Recipient=="regional@infotep.gob.do"));
    }

    [Fact] public async Task VerifiedRegionalTransferClearsOnlyRegionalBlockAndAuditsTheChange()
    {
        await using var app=await Create(true); using var regional=app.Client(); await app.Login(regional,"regional@infotep.gob.do");
        Assert.True((await app.Rpc(regional,"apiSetRegionalUserBlock",new {email="user@infotep.gob.do",blocked=true}))["ok"]!.GetValue<bool>());
        app.Profiles["user@infotep.gob.do"]=new("user@infotep.gob.do","Nombre AD","1002","E","DE","Departamento Este");
        using var user=app.Client(); Assert.Equal("/",await app.Login(user,"user@infotep.gob.do"));
        var init=await app.Rpc(user,"apiGetInitData"); Assert.Equal("east",init["regionalId"]!.ToString());
        using var scope=app.Services.CreateScope(); Assert.True(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Audit.IgnoreQueryFilters().AnyAsync(x=>x.Action=="DirectoryTransferClearedRegionalBlock"));
    }
    [Fact] public async Task VerifiedDirectoryUpdatesNamesWithoutRewritingHistoricalOrderIdentity()
    {
        await using var app=await Create(true);
        using (var scope=app.Services.CreateScope()) { var db=scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.Orders.Add(new Order {Email="user@infotep.gob.do",Name="Nombre histórico",DepartmentId="a",Date="2026-10-12",Status="ACTIVO",Summary="Arroz"}); await db.SaveChangesAsync(); }
        app.Profiles["user@infotep.gob.do"]=new("user@infotep.gob.do","Nombre actualizado AD","1002","C","D1","Departamento renombrado AD");
        using var user=app.Client(); await app.Login(user,"user@infotep.gob.do");
        using var check=app.Services.CreateScope(); var dbCheck=check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("Nombre actualizado AD",(await dbCheck.Users.FindAsync("user@infotep.gob.do"))!.Name);
        Assert.Equal("Departamento renombrado AD",(await dbCheck.Departments.FindAsync("a"))!.Name);
        Assert.Equal("Nombre histórico",(await dbCheck.Orders.SingleAsync()).Name);
        Assert.True(await dbCheck.Audit.AnyAsync(x=>x.Action=="DirectoryDepartmentRenamed"));
    }
    [Fact] public async Task DirectoryDepartmentOwnershipSurvivesTransferOfItsLastVerifiedMember()
    {
        await using var app=await Create(true); using var user=app.Client(); await app.Login(user,"user@infotep.gob.do");
        app.Profiles["user@infotep.gob.do"]=new("user@infotep.gob.do","Nombre AD","1002","E","DE","Departamento Este");
        using var moved=app.Client(); await app.Login(moved,"user@infotep.gob.do");
        app.Profiles["general@infotep.gob.do"]=new("general@infotep.gob.do","General AD","1000","C","D2","Dirección General");
        using var general=app.Client(); await app.Login(general,"general@infotep.gob.do");
        using var scope=app.Services.CreateScope(); var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Users.AnyAsync(x=>x.DepartmentId=="a"&&x.DirectoryManaged));
        Assert.True((await db.Departments.FindAsync("a"))!.DirectoryManaged);
        Assert.False((await app.Rpc(general,"apiSaveDepartment",new {id="a",nombre="Nombre manual",directoryCode="D1",admins=""}))["ok"]!.GetValue<bool>());
        var deletion=await app.Rpc(general,"apiDeleteDepartment","a"); Assert.False(deletion["ok"]!.GetValue<bool>()); Assert.Contains("AD",deletion["msg"]!.ToString());
    }
    [Fact] public async Task ReusedDepartmentLabelsDoNotGrantAccessToForeignIdentifiedFeedback()
    {
        await using var app=await Create();
        using (var scope=app.Services.CreateScope()) {
            var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Orders.AddRange(new Order {Id="own-rating-order",Email="user@infotep.gob.do",Name="Propio",DepartmentId="a",Date="2026-10-12",Status="ACTIVO"},new Order {Id="foreign-rating-order",Email="other@infotep.gob.do",Name="Nombre ajeno",DepartmentId="b",Date="2026-10-12",Status="ACTIVO"});
            db.Ratings.AddRange(new Rating {Email="user@infotep.gob.do",Name="Propio",Kind="MEAL",Scope="2026-10-12",OrderId="own-rating-order",Department="Nombre anterior",Score=4},new Rating {Email="other@infotep.gob.do",Name="Nombre ajeno",Kind="MEAL",Scope="2026-10-12",OrderId="foreign-rating-order",Department="Departamento A",Score=4},new Rating {Email="other@infotep.gob.do",Name="Nombre ajeno",Kind="PROVIDER",Scope="PROV_INITIAL",Department="Departamento A",Score=4});
            await db.SaveChangesAsync();
        }
        using var admin=app.Client(); await app.Login(admin,"admin@infotep.gob.do");
        var summary=await app.Rpc(admin,"apiGetRatingsSummary"); Assert.Contains("Propio",summary.ToJsonString()); Assert.DoesNotContain("Nombre ajeno",summary.ToJsonString());
    }
}
