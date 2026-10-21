using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Almuerzo.Web.Data;
using Almuerzo.Web.Models;
using Almuerzo.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Almuerzo.Tests;

public class DirectoryNamesTests
{
    private static AppFactory App(bool mapped = true)
    {
        var app = new AppFactory();
        app.ExtraConfiguration["Ad:OrganizationContract"] = "NamesV1";
        app.ExtraConfiguration["Ad:AutoProvisioningEnabled"] = "true";
        app.ExtraConfiguration["Ad:AutoCreateOrganizationEnabled"] = "true";
        if (mapped) {
            app.ExtraConfiguration["Ad:OrganizationMappings:0:Name"] = "Regional AD de prueba";
            app.ExtraConfiguration["Ad:OrganizationMappings:0:Id"] = "central";
        }
        foreach (var (name, code) in new[] { ("general","1000"),("admin","1001"),("user","1002"),("other","1003") }) app.Profiles[name+"@infotep.gob.do"]=Profile(name,code);
        return app;
    }
    private static DirectoryIdentity Profile(string account="new",string code="1004",string region="Regional AD de prueba",string department="Departamento AD de prueba")
        => new(account+"@infotep.gob.do","Persona sintética "+account,code,DepartmentName:department,RegionName:region,JobTitle:"Puesto de prueba");

    [Fact] public async Task NewAccountWaitsForGroupAndApprovalEvenAfterRepeatedLogin()
    {
        await using var app=App(); await app.Seed(); app.Profiles["new@infotep.gob.do"]=Profile();
        using var first=app.Client(); Assert.Equal("/Auth/Denied",await app.Login(first,"new@infotep.gob.do"));
        using var second=app.Client(); Assert.Equal("/Auth/Denied",await app.Login(second,"new@infotep.gob.do"));
        using(var scope=app.Services.CreateScope()) {
            var db=scope.ServiceProvider.GetRequiredService<AppDbContext>(); var u=await db.Users.Include(x=>x.DirectoryDepartment).SingleAsync(x=>x.Email=="new@infotep.gob.do");
            Assert.Equal("PENDIENTE",u.Status);Assert.Null(u.DepartmentId);Assert.Equal(Roles.User,u.Role);Assert.Equal("Departamento AD de prueba",u.DirectoryDepartment!.Name);
            Assert.Equal(2,await db.Departments.CountAsync());Assert.Single(await db.Outbox.Where(x=>x.Subject=="Nuevo colaborador pendiente de grupo de almuerzo").ToListAsync());
        }
        using var general=app.Client();await app.Login(general,"general@infotep.gob.do");
        Assert.False((await app.Rpc(general,"apiAdminSetUserStatus",new {email="new@infotep.gob.do",estado="ACTIVO"}))["ok"]!.GetValue<bool>());
        Assert.True((await app.Rpc(general,"apiAdminSaveUser",new {email="new@infotep.gob.do",departamento="a",estado="ACTIVO",rol="USER"}))["ok"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.OK,(await first.GetAsync("/")).StatusCode);
        using var again=app.Client();Assert.Equal("/",await app.Login(again,"new@infotep.gob.do"));
    }

    [Fact] public async Task MultipleADDepartmentsShareLegacyGroupAndResponsibleScope()
    {
        await using var app=App();await app.Seed();
        app.Profiles["admin@infotep.gob.do"]=Profile("admin","1001",department:"Unidad AD Uno");app.Profiles["user@infotep.gob.do"]=Profile("user","1002",department:"Unidad AD Dos");
        using var admin=app.Client();using var member=app.Client();await app.Login(admin,"admin@infotep.gob.do");await app.Login(member,"user@infotep.gob.do");
        var init=await app.Rpc(admin,"apiGetInitData");Assert.Contains("user@infotep.gob.do",init["deptUsers"]!.ToJsonString());
        Assert.True((await app.Rpc(admin,"apiGetInitData","","user@infotep.gob.do"))["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(admin,"apiGetInitData","","other@infotep.gob.do"))["ok"]!.GetValue<bool>());
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();Assert.Equal(2,await db.DirectoryDepartments.CountAsync());
        Assert.Equal("Departamento A",(await db.Departments.FindAsync("a"))!.Name);Assert.Equal("a",(await db.Users.FindAsync("user@infotep.gob.do"))!.DepartmentId);Assert.Equal(Roles.DepartmentAdmin,(await db.Users.FindAsync("admin@infotep.gob.do"))!.Role);
    }

    [Fact] public async Task GroupRenameAndADChangePreserveRolesAndHistoricalOrders()
    {
        await using var app=App();await app.Seed();using var general=app.Client();using var admin=app.Client();await app.Login(general,"general@infotep.gob.do");await app.Login(admin,"admin@infotep.gob.do");
        using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();db.Orders.Add(new Order{Id="historic",DepartmentId="a",Email="user@infotep.gob.do",Name="Histórico",Date="2026-10-12"});await db.SaveChangesAsync();}
        Assert.True((await app.Rpc(general,"apiSaveDepartment",new {id="a",nombre="Grupo sintético",admins="admin@infotep.gob.do",estado="ACTIVO"}))["ok"]!.GetValue<bool>());
        app.Profiles["admin@infotep.gob.do"]=Profile("admin","1001",department:"Otra unidad AD");using var again=app.Client();Assert.Equal("/",await app.Login(again,"admin@infotep.gob.do"));
        using var check=app.Services.CreateScope();var database=check.ServiceProvider.GetRequiredService<AppDbContext>();var u=await database.Users.Include(x=>x.DirectoryDepartment).SingleAsync(x=>x.Email=="admin@infotep.gob.do");
        Assert.Equal("a",u.DepartmentId);Assert.Equal(Roles.DepartmentAdmin,u.Role);Assert.Equal("Otra unidad AD",u.DirectoryDepartment!.Name);Assert.Equal("Grupo sintético",(await database.Departments.FindAsync("a"))!.Name);Assert.Equal("a",(await database.Orders.SingleAsync()).DepartmentId);
    }

    [Fact] public async Task GeneralCanReceiveGroupSummaryWithoutLosingRoleOrIncludingForeignGroup()
    {
        await using var app=App();await app.Seed();using var general=app.Client();await app.Login(general,"general@infotep.gob.do");
        Assert.False((await app.Rpc(general,"apiSaveDepartment",new {id="a",nombre="Grupo A",admins="other@infotep.gob.do"}))["ok"]!.GetValue<bool>());
        Assert.True((await app.Rpc(general,"apiSaveDepartment",new {id="a",nombre="Grupo A",admins="general@infotep.gob.do, admin@infotep.gob.do"}))["ok"]!.GetValue<bool>());
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();Assert.Equal(Roles.GeneralAdmin,(await db.Users.FindAsync("general@infotep.gob.do"))!.Role);
        db.Orders.AddRange(new Order{Email="user@infotep.gob.do",DepartmentId="a",Date="2026-10-12",Name="Miembro A"},new Order{Email="other@infotep.gob.do",DepartmentId="b",Date="2026-10-12",Name="Miembro B"});await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<ReportService>().EnqueueCloseAsync("2026-10-12");await db.SaveChangesAsync();Assert.True(await db.Outbox.AnyAsync(x=>x.Recipient=="general@infotep.gob.do"&&x.Subject.Contains("del grupo")));
        var report=await ReportLayout.Load(db,"2026-10-12","a");Assert.Single(report.Sections);Assert.DoesNotContain(report.Sections.SelectMany(x=>x.People),x=>x.Name=="Miembro B");
    }

    [Fact] public async Task GroupAssignmentCannotChangeADAndRevokesOldScope()
    {
        await using var app=App();await app.Seed();using var general=app.Client();using var admin=app.Client();await app.Login(general,"general@infotep.gob.do");await app.Login(admin,"admin@infotep.gob.do");
        string? adId;using(var scope=app.Services.CreateScope())adId=(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.FindAsync("admin@infotep.gob.do"))!.DirectoryDepartmentId;
        Assert.False((await app.Rpc(general,"apiAdminSaveUser",new {email="admin@infotep.gob.do",adDepartmentId="forged"}))["ok"]!.GetValue<bool>());
        var data=await app.Rpc(general,"apiGetAdminData");var form=data["users"]!.AsArray().Single(x=>x!["email"]!.ToString()=="admin@infotep.gob.do")!.DeepClone();form["departamento"]="b";form["rol"]="USER";
        Assert.True((await app.Rpc(general,"apiAdminSaveUser",form))["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(admin,"apiGetAdminData"))["ok"]!.GetValue<bool>());
        using var check=app.Services.CreateScope();var db=check.ServiceProvider.GetRequiredService<AppDbContext>();Assert.Equal(adId,(await db.Users.FindAsync("admin@infotep.gob.do"))!.DirectoryDepartmentId);
    }

    [Fact] public async Task RegionalCannotAssignGroupsOrApprovePendingUsers()
    {
        await using var app=App();await app.Seed();using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();(await db.Users.FindAsync("admin@infotep.gob.do"))!.Role=Roles.RegionalAdmin;await db.SaveChangesAsync();}
        app.Profiles["new@infotep.gob.do"]=Profile();using var pending=app.Client();await app.Login(pending,"new@infotep.gob.do");using var regional=app.Client();await app.Login(regional,"admin@infotep.gob.do");
        Assert.False((await app.Rpc(regional,"apiAdminSaveUser",new {email="new@infotep.gob.do",departamento="a",estado="ACTIVO"}))["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(regional,"apiSaveDepartment",new {id="a",nombre="Ataque"}))["ok"]!.GetValue<bool>());
    }

    [Fact] public async Task RegionalMappingPreservesImportedContactAndMealGroup()
    {
        await using var app=App();await app.Seed();app.Profiles["alias@infotep.gob.do"]=Profile("alias","1000");using var client=app.Client();Assert.Equal("/",await app.Login(client,"alias@infotep.gob.do"));
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var u=await db.Users.FindAsync("general@infotep.gob.do");Assert.Equal("alias@infotep.gob.do",u!.DirectoryAccount);Assert.Equal("a",u.DepartmentId);Assert.Equal(Roles.GeneralAdmin,u.Role);Assert.Single(await db.Regions.ToListAsync());
    }

    [Fact] public async Task SwitchingLoginFromAnotherRegionCannotMisplaceCentralDirectoryRecords()
    {
        await using var app=App();await app.Seed();app.Profiles["new@infotep.gob.do"]=Profile(region:"Regional diferente");using var client=app.Client();Assert.Equal("/Auth/Denied",await app.Login(client,"new@infotep.gob.do"));
        app.Profiles["general@infotep.gob.do"]=Profile("general","1000",department:"Unidad central verificada");Assert.Equal("/",await app.Login(client,"general@infotep.gob.do"));
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var u=await db.Users.Include(x=>x.DirectoryDepartment).SingleAsync(x=>x.Email=="general@infotep.gob.do");Assert.Equal("central",u.RegionId);Assert.Equal("central",u.DirectoryDepartment!.RegionId);
    }

    [Fact] public async Task UnmappedImportedRegionalFailsAtomically()
    {
        await using var app=App(false);await app.Seed();using var client=app.Client();var html=await client.GetStringAsync("/Auth/Login");
        var response=await client.PostAsync("/Auth/Login",new FormUrlEncodedContent(new Dictionary<string,string>{["Username"]="general@infotep.gob.do",["Password"]="test-only",["__RequestVerificationToken"]=AppFactory.Token(html)}));Assert.Contains("mapear",await response.Content.ReadAsStringAsync());
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();Assert.Single(await db.Regions.ToListAsync());Assert.Empty(await db.DirectoryDepartments.ToListAsync());Assert.Equal("a",(await db.Users.FindAsync("general@infotep.gob.do"))!.DepartmentId);
    }

    [Fact] public async Task RegionalTransferRequiresNewGroupAndPreservesHistory()
    {
        await using var app=App();await app.Seed();using var admin=app.Client();await app.Login(admin,"admin@infotep.gob.do");
        using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var u=await db.Users.FindAsync("admin@infotep.gob.do");u!.RegionalBlocked=true;db.Orders.Add(new Order{Id="old-context",DepartmentId="a",Email=u.Email,Name=u.Name,Date="2026-10-12"});await db.SaveChangesAsync();}
        app.Profiles["admin@infotep.gob.do"]=Profile("admin","1001",region:"Otra regional");using var moved=app.Client();Assert.Equal("/Auth/Denied",await app.Login(moved,"admin@infotep.gob.do"));
        using var check=app.Services.CreateScope();var dbCheck=check.ServiceProvider.GetRequiredService<AppDbContext>();var user=await dbCheck.Users.IgnoreQueryFilters().SingleAsync(x=>x.Email=="admin@infotep.gob.do");
        Assert.Null(user.DepartmentId);Assert.Equal(Roles.User,user.Role);Assert.Equal("PENDIENTE",user.Status);Assert.False(user.RegionalBlocked);Assert.NotEqual("central",user.RegionId);Assert.Equal("central",(await dbCheck.Orders.IgnoreQueryFilters().SingleAsync(x=>x.Id=="old-context")).RegionId);
    }

    [Fact] public async Task NewRegionGetsDefaultsAndADCatalogWithoutMealGroupOrPrivileges()
    {
        await using var app=App(false);await app.Seed();app.Profiles["new@infotep.gob.do"]=Profile();using var client=app.Client();Assert.Equal("/Auth/Denied",await app.Login(client,"new@infotep.gob.do"));
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var user=await db.Users.IgnoreQueryFilters().SingleAsync(x=>x.Email=="new@infotep.gob.do");db.RegionId=user.RegionId;
        Assert.Equal(Roles.User,user.Role);Assert.Empty(await db.Departments.ToListAsync());Assert.Single(await db.DirectoryDepartments.ToListAsync());Assert.Equal(9,await db.Categories.CountAsync());
    }

    [Fact] public async Task InactiveAndSuspendedUsersCannotBeApprovedByReauthentication()
    {
        await using var app=App();await app.Seed();using var general=app.Client();await app.Login(general,"general@infotep.gob.do");
        Assert.True((await app.Rpc(general,"apiAdminSetUserStatus",new {email="user@infotep.gob.do",estado="INACTIVO"}))["ok"]!.GetValue<bool>());
        Assert.True((await app.Rpc(general,"apiSetRegionalUserBlock",new {email="admin@infotep.gob.do",blocked=true}))["ok"]!.GetValue<bool>());
        using var inactive=app.Client();using var suspended=app.Client();Assert.Equal("/Auth/Denied",await app.Login(inactive,"user@infotep.gob.do"));Assert.Equal("/Auth/Denied",await app.Login(suspended,"admin@infotep.gob.do"));
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();Assert.Equal("INACTIVO",(await db.Users.FindAsync("user@infotep.gob.do"))!.Status);Assert.True((await db.Users.FindAsync("admin@infotep.gob.do"))!.RegionalBlocked);
    }

    [Fact] public async Task ReassigningUserDoesNotLetNewGroupCancelTheirHistoricalOrder()
    {
        await using var app=App();await app.Seed();using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();db.Orders.Add(new Order{Id="old-group-order",Email="other@infotep.gob.do",DepartmentId="b",Date="2026-10-12",Name="Pedido anterior"});await db.SaveChangesAsync();}
        using var general=app.Client();using var admin=app.Client();await app.Login(general,"general@infotep.gob.do");await app.Login(admin,"admin@infotep.gob.do");
        Assert.True((await app.Rpc(general,"apiAdminSaveUser",new {email="other@infotep.gob.do",departamento="a"}))["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(admin,"apiCancelOrder","old-group-order"))["ok"]!.GetValue<bool>());Assert.False((await app.Rpc(admin,"apiAdminCancelOrder","old-group-order"))["ok"]!.GetValue<bool>());
        var view=await app.Rpc(admin,"apiGetInitData","","other@infotep.gob.do");Assert.Empty(view["allOrders"]!.AsObject());
    }

    [Fact] public async Task JsonLoginHasNoRedirectResponseAndStillRequiresCSRF()
    {
        await using var app=App();await app.Seed();using var client=app.Client();client.DefaultRequestHeaders.Accept.ParseAdd("application/json");var html=await client.GetStringAsync("/Auth/Login");
        using var invalid=await client.PostAsync("/Auth/Login",new FormUrlEncodedContent(new Dictionary<string,string>{["Username"]="general@infotep.gob.do",["Password"]="wrong",["__RequestVerificationToken"]=AppFactory.Token(html)}));Assert.Null(invalid.Headers.Location);Assert.False((await invalid.Content.ReadFromJsonAsync<JsonObject>())!["ok"]!.GetValue<bool>());
        using var response=await client.PostAsync("/Auth/Login",new FormUrlEncodedContent(new Dictionary<string,string>{["Username"]="general@infotep.gob.do",["Password"]="test-only",["__RequestVerificationToken"]=AppFactory.Token(html)}));Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Null(response.Headers.Location);Assert.Equal("/",(await response.Content.ReadFromJsonAsync<JsonObject>())!["redirectUrl"]!.ToString());Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/")).StatusCode);
        using var noCsrf=await client.PostAsync("/Auth/Login",new FormUrlEncodedContent(new Dictionary<string,string>{["Username"]="general@infotep.gob.do",["Password"]="test-only"}));Assert.Equal(HttpStatusCode.BadRequest,noCsrf.StatusCode);
    }

    [Fact] public void NameKeyPreservesAccentsPunctuationAndUnicodeComposition()
    {
        Assert.Equal(DirectoryCatalog.Key(" Región  Central "),DirectoryCatalog.Key("REGIÓN CENTRAL"));Assert.NotEqual(DirectoryCatalog.Key("Región Central"),DirectoryCatalog.Key("Region Central"));Assert.NotEqual(DirectoryCatalog.Key("252- Equipo"),DirectoryCatalog.Key("252 Equipo"));
    }
}
