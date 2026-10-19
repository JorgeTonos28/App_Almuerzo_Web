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

    public static async Task<(Region Region, Department Department, bool Created)> Resolve(AppDbContext db, IConfiguration config, DirectoryIdentity identity, AppUser? user, CancellationToken ct)
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
            var mapped = await db.Regions.SingleOrDefaultAsync(x => x.Id == mapping["Id"], ct) ?? throw new BusinessException("La regional del mapeo no existe.");
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
        var department = await db.Departments.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.RegionId == region.Id && x.DirectoryKey == departmentKey, ct);
        var departmentMappings = mapping?.GetSection("Departments").GetChildren().Where(x => Key(x["Name"] ?? "") == departmentKey).ToArray() ?? [];
        LunchService.Require(departmentMappings.Length <= 1, "El mapeo del departamento requiere revisión de TI.");
        if (departmentMappings.SingleOrDefault() is { } departmentMapping)
        {
            var mapped = await db.Departments.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.RegionId == region.Id && x.Id == departmentMapping["Id"], ct) ?? throw new BusinessException("El departamento del mapeo no existe en esta regional.");
            LunchService.Require((department == null || department.Id == mapped.Id) && (mapped.DirectoryKey == null || mapped.DirectoryKey == departmentKey), "El mapeo departamental entra en conflicto con una vinculación AD.");
            department = mapped;
        }
        if (user?.DepartmentId != null && user.DepartmentId != department?.Id)
        {
            var previous = await db.Departments.IgnoreQueryFilters().SingleAsync(x => x.Id == user.DepartmentId, ct);
            LunchService.Require(previous.DirectoryKey != null, "TI debe mapear el departamento importado antes del primer acceso con el nuevo AD.");
        }
        if (department == null)
        {
            LunchService.Require(config.GetValue<bool>("Ad:AutoCreateOrganizationEnabled"), "La creación de organizaciones AD no está habilitada.");
            LunchService.Require(!await db.Departments.IgnoreQueryFilters().AnyAsync(x => x.RegionId == region.Id && x.Name == departmentName, ct), "TI debe mapear el departamento existente con su nombre AD.");
            var legacyNames = await db.Departments.IgnoreQueryFilters().Where(x => x.RegionId == region.Id && x.DirectoryKey == null).Select(x => x.Name).ToListAsync(ct);
            LunchService.Require(!legacyNames.Any(x => Key(x) == departmentKey), "TI debe mapear el departamento existente con su nombre AD.");
            department = new Department { RegionId = region.Id, Name = departmentName, DirectoryKey = departmentKey, DirectoryManaged = true };
            db.Departments.Add(department);
            Audit("DirectoryCreateDepartment", department.Id);
        }
        LunchService.Require(department.Status == "ACTIVO", "El departamento institucional está inactivo.");
        // Mapping only selects a legacy ID. Canonical names are assigned from the authenticated profile.
        LunchService.Require(!await db.Regions.AnyAsync(x => x.Id != region.Id && x.Name == regionName, ct)
            && !await db.Departments.IgnoreQueryFilters().AnyAsync(x => x.RegionId == region.Id && x.Id != department.Id && x.Name == departmentName, ct), "Los nombres institucionales requieren reconciliación con TI.");
        if (region.DirectoryKey == null) Audit("DirectoryLinkRegion", region.Id);
        if (department.DirectoryKey == null) Audit("DirectoryLinkDepartment", department.Id);
        if (region.DirectoryKey != null && region.Name != regionName) Audit("DirectoryRefreshRegionName", region.Id);
        if (department.DirectoryKey != null && department.Name != departmentName) Audit("DirectoryRefreshDepartmentName", department.Id);
        region.Name = regionName; region.DirectoryKey = regionKey; region.DirectoryManaged = true;
        department.Name = departmentName; department.DirectoryKey = departmentKey; department.DirectoryManaged = true;
        return (region, department, created);

        void Audit(string action, string id) => db.Audit.Add(new AuditEvent { RegionId = region!.Id, Actor = identity.Email, Action = action, EntityId = id });
    }
}
