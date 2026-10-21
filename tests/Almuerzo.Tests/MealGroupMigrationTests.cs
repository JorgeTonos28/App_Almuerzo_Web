using Almuerzo.Web.Data;
using Almuerzo.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Almuerzo.Tests;

public class MealGroupMigrationTests
{
    [Fact] public async Task UpgradeSeparatesOnlyVerifiedADLinksWithoutChangingLegacyGroupsOrRoles()
    {
        await using var app=new AppFactory();
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var migrator=db.GetService<IMigrator>();
        var previous=db.Database.IsSqlServer()?"20261019175801_DirectoryAliasesAndNamesSqlServer":"20261019175731_DirectoryAliasesAndNames";
        await migrator.MigrateAsync(previous);
        var key=DirectoryCatalog.Key("Unidad AD sintética");
        await db.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO Departments (Id, RegionId, Name, Alias, DirectoryKey, DirectoryCode, DirectoryManaged, Admins, Status, PreferencesJson)
            VALUES ('legacy-group', 'central', 'Unidad AD sintética', 'Grupo sintético', {{key}}, NULL, 1, 'responsable@infotep.gob.do', 'ACTIVO', '{}');
            INSERT INTO Users (Email, RegionId, Name, DepartmentId, Role, Status, EmployeeCode, DirectoryAccount, JobTitle, PreferencesJson, GameJson, Revision, DirectoryManaged, RegionalBlocked)
            VALUES ('responsable@infotep.gob.do','central','Responsable','legacy-group','ADMIN_DEP','ACTIVO','8001','responsable@infotep.gob.do','','{}','{}','r1',1,0);
            INSERT INTO Users (Email, RegionId, Name, DepartmentId, Role, Status, EmployeeCode, DirectoryAccount, JobTitle, PreferencesJson, GameJson, Revision, DirectoryManaged, RegionalBlocked)
            VALUES ('sinverificar@infotep.gob.do','central','Importado','legacy-group','USER','ACTIVO','8002',NULL,'','{}','{}','r2',0,0);
            """);
        await migrator.MigrateAsync();
        var group=await db.Departments.SingleAsync();Assert.Equal("legacy-group",group.Id);Assert.Equal("Grupo sintético",group.DisplayName);Assert.Equal("responsable@infotep.gob.do",group.Admins);
        var directory=await db.DirectoryDepartments.SingleAsync();Assert.Equal("Unidad AD sintética",directory.Name);Assert.Equal(key,directory.DirectoryKey);
        var responsible=await db.Users.SingleAsync(x=>x.Email=="responsable@infotep.gob.do");Assert.Equal(group.Id,responsible.DepartmentId);Assert.Equal(directory.Id,responsible.DirectoryDepartmentId);Assert.Equal("ADMIN_DEP",responsible.Role);Assert.Equal("ACTIVO",responsible.Status);
        Assert.Null((await db.Users.SingleAsync(x=>x.Email=="sinverificar@infotep.gob.do")).DirectoryDepartmentId);
    }
}
