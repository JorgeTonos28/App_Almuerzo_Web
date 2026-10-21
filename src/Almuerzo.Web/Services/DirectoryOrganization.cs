using Almuerzo.Web.Data;
using Almuerzo.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Almuerzo.Web.Services;

public class DirectoryOrganization(AppDbContext db, IConfiguration config)
{
    private string? createdRegion;
    public async Task InitializeCreatedRegion()
    {
        if (createdRegion == null) return;
        var previous = db.RegionId;
        try { db.RegionId = createdRegion; await SeedData.InitializeAsync(db); }
        finally { db.RegionId = previous; }
    }
    public async Task<AppUser?> Synchronize(DirectoryIdentity identity, AppUser? user, CancellationToken ct)
    {
        if (!config.GetValue<bool>("Ad:AutoProvisioningEnabled")) return user;
        if (user?.Status == "INACTIVO") return user;
        if (user is { Status: "PENDIENTE" } && user.Role != Roles.User) return user;
        Region region; Department? department;
        if (config["Ad:OrganizationContract"] == "NamesV1")
        {
            var catalog = await DirectoryCatalog.Resolve(db, config, identity, user, ct);
            region = catalog.Region;
            if (catalog.Created) createdRegion = region.Id;
            var isNew = user == null;
            if (user == null) { user = new AppUser { Email = identity.Email, Role = Roles.User }; db.Users.Add(user); }
            var transferred = user.RegionId != region.Id;
            var organizationChanged = isNew || !user.DirectoryManaged || transferred || user.DirectoryDepartmentId != catalog.Department.Id;
            if (transferred)
            {
                if (user.DepartmentId != null)
                {
                    var previous = await db.Departments.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == user.DepartmentId, ct);
                    if (previous != null) previous.Admins = string.Join(", ", LunchService.Split(previous.Admins).Where(e => !e.Equals(user.Email, StringComparison.OrdinalIgnoreCase)));
                }
                user.DepartmentId = null; user.Department = null;
                if (user.Role is Roles.DepartmentAdmin or Roles.RegionalAdmin) user.Role = Roles.User;
                user.Status = "PENDIENTE";
                if (user.RegionalBlocked) {
                    user.RegionalBlocked = false;
                    db.Audit.Add(new AuditEvent { RegionId = region.Id, Actor = identity.Email, Action = "DirectoryTransferClearedRegionalBlock", EntityId = user.Email });
                }
            }
            // Existing meal groups and responsibilities survive changes of AD department in the same region.
            // New accounts never become active merely by authenticating or matching an imported department name.
            user.RegionId = region.Id; user.DirectoryDepartment = catalog.Department; user.DirectoryDepartmentId = catalog.Department.Id; user.DirectoryManaged = true;
            if (isNew || user.DepartmentId == null) user.Status = "PENDIENTE";
            if (organizationChanged) db.Audit.Add(new AuditEvent { RegionId = region.Id, Actor = user.Email, Action = isNew ? "DirectoryProvisionPendingUser" : "DirectorySyncOrganization", EntityId = user.Email });
            if (isNew) {
                var admins = await db.Users.IgnoreQueryFilters().Where(x => x.Role == Roles.GeneralAdmin && x.Status == "ACTIVO" && !x.RegionalBlocked && x.Department!.Status == "ACTIVO").Select(x => x.Email).ToListAsync(ct);
                foreach (var admin in admins.Distinct()) db.Outbox.Add(new MailMessageRecord { RegionId = region.Id, Recipient = admin, Subject = "Nuevo colaborador pendiente de grupo de almuerzo", Body = $"{identity.Name} ({identity.Email}) validó su identidad institucional. Revisa su regional y departamento AD, asígnale un grupo de almuerzo y habilita su acceso." });
            }
            return user;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(identity.RegionCode) || string.IsNullOrWhiteSpace(identity.DepartmentCode) || string.IsNullOrWhiteSpace(identity.DepartmentName)) throw new BusinessException("El AD no devolvió una regional y departamento verificables.");
            region = await db.Regions.SingleOrDefaultAsync(x => x.DirectoryCode == identity.RegionCode && x.Status == "ACTIVO", ct)
                ?? throw new BusinessException("La regional institucional todavía no está configurada. Contacta a TI.");
            department = await db.Departments.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.RegionId == region.Id && x.DirectoryCode == identity.DepartmentCode, ct);
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
