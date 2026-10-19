using Almuerzo.Web.Data;
using Almuerzo.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Almuerzo.Web.Services;

public class DirectoryOrganization(AppDbContext db, IConfiguration config)
{
    public async Task<AppUser?> Synchronize(DirectoryIdentity identity, AppUser? user, CancellationToken ct)
    {
        if (!config.GetValue<bool>("Ad:AutoProvisioningEnabled")) return user;
        if (string.IsNullOrWhiteSpace(identity.RegionCode) || string.IsNullOrWhiteSpace(identity.DepartmentCode) || string.IsNullOrWhiteSpace(identity.DepartmentName)) throw new BusinessException("El AD no devolvió una regional y departamento verificables.");
        var region = await db.Regions.SingleOrDefaultAsync(x => x.DirectoryCode == identity.RegionCode && x.Status == "ACTIVO", ct)
            ?? throw new BusinessException("La regional institucional todavía no está configurada. Contacta a TI.");
        // Explicit deactivation remains authoritative, even when AD still authenticates the account.
        if (user?.Status == "INACTIVO") return user;
        if (user is { Status: "PENDIENTE" } && user.Role != Roles.User) return user;
        var department = await db.Departments.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.RegionId == region.Id && x.DirectoryCode == identity.DepartmentCode, ct);
        if (department == null)
        {
            if (await db.Departments.IgnoreQueryFilters().AnyAsync(x => x.RegionId == region.Id && x.Name == identity.DepartmentName, ct)) throw new BusinessException("TI debe vincular el código AD al departamento existente.");
            department = new Department { RegionId = region.Id, DirectoryCode = LunchService.Bounded(identity.DepartmentCode, 64, "Código AD"), Name = LunchService.Bounded(identity.DepartmentName, 150, "Departamento") };
            db.Departments.Add(department);
        }
        if (department.Status != "ACTIVO") throw new BusinessException("El departamento institucional está inactivo.");
        region.DirectoryManaged = true; department.DirectoryManaged = true;
        if (department.Name != identity.DepartmentName) {
            if (await db.Departments.IgnoreQueryFilters().AnyAsync(x => x.RegionId == region.Id && x.Id != department.Id && x.Name == identity.DepartmentName, ct)) throw new BusinessException("El nombre institucional requiere reconciliación con TI.");
            department.Name = LunchService.Bounded(identity.DepartmentName, 150, "Departamento");
            db.Audit.Add(new AuditEvent { RegionId = region.Id, Actor = identity.Email, Action = "DirectoryDepartmentRenamed", EntityId = department.Id });
        }
        var action = user == null ? "DirectoryProvisionUser" : "DirectorySyncOrganization";
        if (user == null) { user = new AppUser { Email = identity.Email, Role = Roles.User }; db.Users.Add(user); }
        else if (user.Role == Roles.DepartmentAdmin && user.DepartmentId != department.Id || user.Role == Roles.RegionalAdmin && user.RegionId != region.Id) {
            if (user.DepartmentId != null) {
                var previous = await db.Departments.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == user.DepartmentId, ct);
                if (previous != null) previous.Admins = string.Join(", ", LunchService.Split(previous.Admins).Where(e => !e.Equals(user.Email, StringComparison.OrdinalIgnoreCase)));
            }
            user.Role = Roles.User;
        }
        var changed = !user.DirectoryManaged || user.DepartmentId != department.Id || user.RegionId != region.Id;
        if (user.RegionId != region.Id && user.RegionalBlocked) {
            user.RegionalBlocked = false;
            db.Audit.Add(new AuditEvent { RegionId = region.Id, Actor = identity.Email, Action = "DirectoryTransferClearedRegionalBlock", EntityId = user.Email });
        }
        user.RegionId = region.Id; user.Department = department; user.DepartmentId = department.Id;
        user.DirectoryManaged = true; user.Status = "ACTIVO";
        if (changed) db.Audit.Add(new AuditEvent { RegionId = region.Id, Actor = user.Email, Action = action, EntityId = user.Email });
        return user;
    }
}
