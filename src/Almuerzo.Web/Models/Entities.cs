namespace Almuerzo.Web.Models;

public class BrandingAsset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public byte[] Content { get; set; } = [];
}

public static class Roles
{
    public const string User = "USER", DepartmentAdmin = "ADMIN_DEP", RegionalAdmin = "ADMIN_REG", GeneralAdmin = "ADMIN_GEN";
    public static readonly string[] All = [User, DepartmentAdmin, RegionalAdmin, GeneralAdmin];
}

public interface IRegionalEntity { string RegionId { get; set; } }
public class Region
{
    public bool DirectoryManaged { get; set; }
    public const string LegacyId = "central";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Alias { get; set; } = "";
    public string? DirectoryKey { get; set; }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped] public string DisplayName => Alias.Length > 0 ? Alias : Name;
    public string? DirectoryCode { get; set; }
    public string Status { get; set; } = "ACTIVO";
}

// Legacy Department IDs are meal groups, not institutional AD departments.
public class Department : IRegionalEntity
{
    public bool DirectoryManaged { get; set; }
    public string Alias { get; set; } = "";
    public string? DirectoryKey { get; set; }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped] public string DisplayName => Alias.Length > 0 ? Alias : Name;
    public string? DirectoryCode { get; set; }
    public string RegionId { get; set; } = Region.LegacyId;
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Admins { get; set; } = "";
    public string Status { get; set; } = "ACTIVO";
    public string PreferencesJson { get; set; } = "{}";
}

public class DirectoryDepartment : IRegionalEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string RegionId { get; set; } = Region.LegacyId;
    public string Name { get; set; } = "";
    public string? DirectoryKey { get; set; }
    public string? DirectoryCode { get; set; }
    public string Status { get; set; } = "ACTIVO";
}

public class AppUser : IRegionalEntity
{
    public bool DirectoryManaged { get; set; }
    public bool RegionalBlocked { get; set; }
    public string RegionId { get; set; } = Region.LegacyId;
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public string? DirectoryDepartmentId { get; set; }
    public DirectoryDepartment? DirectoryDepartment { get; set; }
    public string Role { get; set; } = Roles.User;
    public string Status { get; set; } = "PENDIENTE";
    public string? EmployeeCode { get; set; }
    public string? DirectoryAccount { get; set; }
    public string JobTitle { get; set; } = "";
    public string PreferencesJson { get; set; } = "{}";
    public string GameJson { get; set; } = "{}";
    public string Revision { get; set; } = Guid.NewGuid().ToString();
}

public class MenuCategory : IRegionalEntity
{
    public string RegionId { get; set; } = Region.LegacyId;
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public string Status { get; set; } = "ACTIVO";
    public string Aliases { get; set; } = "";
    public bool Combinable { get; set; } = true;
    public string CombinableWith { get; set; } = "";
    public string SelectionType { get; set; } = "UNICA";
}

public class MenuItem : IRegionalEntity
{
    public string RegionId { get; set; } = Region.LegacyId;
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Date { get; set; } = "";
    public string CategoryId { get; set; } = "";
    public MenuCategory? Category { get; set; }
    public string Dish { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public class Order : IRegionalEntity
{
    public string RegionId { get; set; } = Region.LegacyId;
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string RequestedAt { get; set; } = "";
    public string Date { get; set; } = "";
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string DepartmentId { get; set; } = "";
    public string Summary { get; set; } = "";
    public string DetailJson { get; set; } = "{}";
    public string Status { get; set; } = "ACTIVO";
    public string ModifiedAt { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public string Revision { get; set; } = Guid.NewGuid().ToString();
}

public class Holiday : IRegionalEntity
{
    public string RegionId { get; set; } = Region.LegacyId;
    public string Date { get; set; } = "";
    public string Reason { get; set; } = "";
}

public class Setting : IRegionalEntity
{
    public string RegionId { get; set; } = Region.LegacyId;
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string Description { get; set; } = "";
}

public class Rating : IRegionalEntity
{
    public string RegionId { get; set; } = Region.LegacyId;
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Kind { get; set; } = "MEAL";
    public string Scope { get; set; } = "";
    public string OrderId { get; set; } = "";
    public string ProviderName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string Department { get; set; } = "";
    public int Score { get; set; }
    public string Comment { get; set; } = "";
    public string Dishes { get; set; } = "";
    public int Version { get; set; } = 1;
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

public class RatingHistory : IRegionalEntity
{
    public string RegionId { get; set; } = Region.LegacyId;
    public long Id { get; set; }
    public string VoteId { get; set; } = "";
    public string PeriodId { get; set; } = "";
    public string ProviderName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string Department { get; set; } = "";
    public int Score { get; set; }
    public string Comment { get; set; } = "";
    public string Timestamp { get; set; } = "";
}

public class AuditEvent : IRegionalEntity
{
    public string RegionId { get; set; } = Region.LegacyId;
    public long Id { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Timestamp { get; set; } = DateTimeOffset.UtcNow.ToString("O");
}

public class MailMessageRecord : IRegionalEntity
{
    public string RegionId { get; set; } = Region.LegacyId;
    public long Id { get; set; }
    public string Recipient { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string AttachmentPath { get; set; } = "";
    public string ExtraAttachmentsJson { get; set; } = "[]";
    public int Attempts { get; set; }
    public long NextAttemptUtcTicks { get; set; }
    public string? SentAt { get; set; }
    public string CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");
}

public class ImportRecord : IRegionalEntity
{
    public string RegionId { get; set; } = Region.LegacyId;
    public string Hash { get; set; } = "";
    public string ImportedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");
    public string SummaryJson { get; set; } = "{}";
}

public class SourceRecord : IRegionalEntity
{
    public string RegionId { get; set; } = Region.LegacyId;
    public long Id { get; set; }
    public string Sheet { get; set; } = "";
    public int Row { get; set; }
    public string Json { get; set; } = "{}";
}

public class JobRun : IRegionalEntity
{
    public string RegionId { get; set; } = Region.LegacyId;
    public string Key { get; set; } = "";
    public string CompletedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");
}
