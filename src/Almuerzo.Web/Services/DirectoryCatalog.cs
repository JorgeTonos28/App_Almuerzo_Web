using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Almuerzo.Web.Data;
using Almuerzo.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Almuerzo.Web.Services;

// NamesV1 provides no stable organizational IDs. Never match on aliases or infer IDs from prefixes.
public static class DirectoryCatalog
{
    public static string Key(string name)
    {
        LunchService.Require(!string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 150 && !name.Any(char.IsControl), "Nombre organizativo AD inválido.");
        var normalized = Regex.Replace(name.Trim().Normalize(NormalizationForm.FormC), @"\s+", " ").ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    public static async Task<(Region Region, DirectoryDepartment Department, bool Created)> Resolve(AppDbContext db, IConfiguration config, DirectoryIdentity identity, AppUser? user, CancellationToken ct)
    {
        var regionName = identity.RegionName ?? throw new BusinessException("AD no devolvió la regional.");
        var departmentName = identity.DepartmentName ?? throw new BusinessException("AD no devolvió el departamento.");
        var regionKey = Key(regionName); var departmentKey = Key(departmentName);
        var mappings = config.GetSection("Ad:OrganizationMappings").GetChildren().Where(x => Key(x["Name"] ?? "") == regionKey).ToArray();
        LunchService.Require(mappings.Length <= 1, "El mapeo organizativo requiere revisión de TI.");
        var mapping = mappings.SingleOrDefault();
        var region = await db.Regions.SingleOrDefaultAsync(x => x.DirectoryKey == regionKey, ct);
        if (mapping != null)
        {
            var mappedId = mapping["Id"]?.Trim();
            var mapped = !string.IsNullOrWhiteSpace(mappedId) ? await db.Regions.SingleOrDefaultAsync(x => x.Id == mappedId, ct) : null;
            if (mapped == null) throw new BusinessException("La regional del mapeo no existe en la base local. Ejecuta scripts\\Configure.cmd map y usa el ID interno de la tabla de regionales.");
            LunchService.Require((region == null || region.Id == mapped.Id) && (mapped.DirectoryKey == null || mapped.DirectoryKey == regionKey), "El mapeo regional entra en conflicto con una vinculación AD.");
            region = mapped;
        }
        // First linking of imported accounts must preserve their catalog IDs and histories explicitly.
        if (user?.DepartmentId != null && (region == null || region.Id != user.RegionId))
        {
            var previous = await db.Regions.FindAsync([user.RegionId], ct);
            LunchService.Require(previous?.DirectoryKey != null, "TI debe mapear la regional importada antes del primer acceso con el nuevo AD.");
        }
        var created = region == null;
        if (region == null)
        {
            LunchService.Require(config.GetValue<bool>("Ad:AutoCreateOrganizationEnabled"), "La creación de organizaciones AD no está habilitada.");
            LunchService.Require(!await db.Regions.AnyAsync(x => x.Name == regionName, ct), "TI debe mapear la regional existente con su nombre AD.");
            var legacyNames = await db.Regions.Where(x => x.DirectoryKey == null).Select(x => x.Name).ToListAsync(ct);
            LunchService.Require(!legacyNames.Any(x => Key(x) == regionKey), "TI debe mapear la regional existente con su nombre AD.");
            region = new Region { Name = regionName, DirectoryKey = regionKey, DirectoryManaged = true };
            db.Regions.Add(region);
            Audit("DirectoryCreateRegion", region.Id);
        }
        LunchService.Require(region.Status == "ACTIVO", "La regional institucional está inactiva.");
        // Institutional departments never select or rename a meal group.
        // Deprecated Departments mappings refer to legacy meal group IDs and are not AD catalog IDs.
        var department = await db.DirectoryDepartments.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.RegionId == region.Id && x.DirectoryKey == departmentKey, ct);
        if (department == null)
        {
            LunchService.Require(config.GetValue<bool>("Ad:AutoCreateOrganizationEnabled"), "La creación de organizaciones AD no está habilitada.");
            department = new DirectoryDepartment { RegionId = region.Id, Name = departmentName, DirectoryKey = departmentKey };
            db.DirectoryDepartments.Add(department);
            Audit("DirectoryCreateDepartment", department.Id);
        }
        LunchService.Require(department.Status == "ACTIVO", "El departamento institucional está inactivo.");
        // Mapping only selects a legacy ID. Canonical names are assigned from the authenticated profile.
        LunchService.Require(!await db.Regions.AnyAsync(x => x.Id != region.Id && x.Name == regionName, ct)
            && !await db.DirectoryDepartments.IgnoreQueryFilters().AnyAsync(x => x.RegionId == region.Id && x.Id != department.Id && x.Name == departmentName, ct), "Los nombres institucionales requieren reconciliación con TI.");
        if (region.DirectoryKey == null) Audit("DirectoryLinkRegion", region.Id);
        if (department.DirectoryKey == null) Audit("DirectoryLinkDepartment", department.Id);
        if (region.DirectoryKey != null && region.Name != regionName) Audit("DirectoryRefreshRegionName", region.Id);
        if (department.DirectoryKey != null && department.Name != departmentName) Audit("DirectoryRefreshDepartmentName", department.Id);
        region.Name = regionName; region.DirectoryKey = regionKey; region.DirectoryManaged = true;
        department.Name = departmentName; department.DirectoryKey = departmentKey;
        return (region, department, created);

        void Audit(string action, string id) => db.Audit.Add(new AuditEvent { RegionId = region!.Id, Actor = identity.Email, Action = action, EntityId = id });
    }
}
