using Almuerzo.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Almuerzo.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    protected AppDbContext(DbContextOptions options) : base(options) { }
    public string RegionId { get; set; } = Region.LegacyId;
    // Only offline migration and the verified directory synchronizer may cross regional boundaries.
    public bool AllowCrossRegionWrites { get; set; }
    public DbSet<Region> Regions => Set<Region>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<MenuCategory> Categories => Set<MenuCategory>();
    public DbSet<MenuItem> Menu => Set<MenuItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<Rating> Ratings => Set<Rating>();
    public DbSet<RatingHistory> RatingHistory => Set<RatingHistory>();
    public DbSet<AuditEvent> Audit => Set<AuditEvent>();
    public DbSet<MailMessageRecord> Outbox => Set<MailMessageRecord>();
    public DbSet<ImportRecord> Imports => Set<ImportRecord>();
    public DbSet<SourceRecord> SourceRecords => Set<SourceRecord>();
    public DbSet<JobRun> JobRuns => Set<JobRun>();
    public DbSet<BrandingAsset> BrandingAssets => Set<BrandingAsset>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Region>().Property(x => x.Id).HasMaxLength(32);
        b.Entity<Region>().Property(x => x.Name).HasMaxLength(150);
        b.Entity<Region>().Property(x => x.DirectoryCode).HasMaxLength(64);
        b.Entity<Region>().Property(x => x.Alias).HasMaxLength(150);
        b.Entity<Region>().Property(x => x.DirectoryKey).HasMaxLength(64);
        b.Entity<Region>().HasIndex(x => x.DirectoryKey).IsUnique().HasFilter("DirectoryKey IS NOT NULL");
        b.Entity<Region>().HasIndex(x => x.Name).IsUnique();
        b.Entity<Region>().HasIndex(x => x.DirectoryCode).IsUnique().HasFilter("DirectoryCode IS NOT NULL");
        b.Entity<AppUser>().HasKey(x => x.Email);
        b.Entity<AppUser>().HasIndex(x => x.EmployeeCode).IsUnique().HasFilter("EmployeeCode IS NOT NULL");
        b.Entity<AppUser>().HasIndex(x => x.DirectoryAccount).IsUnique().HasFilter("DirectoryAccount IS NOT NULL");
        b.Entity<AppUser>().HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AppUser>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<AppUser>().Property(x => x.JobTitle).HasMaxLength(150);
        b.Entity<Department>().HasIndex(x => new { x.RegionId, x.Name }).IsUnique();
        b.Entity<Department>().Property(x => x.DirectoryCode).HasMaxLength(64);
        b.Entity<Department>().Property(x => x.Alias).HasMaxLength(150);
        b.Entity<Department>().Property(x => x.DirectoryKey).HasMaxLength(64);
        b.Entity<Department>().HasIndex(x => new { x.RegionId, x.DirectoryKey }).IsUnique().HasFilter("DirectoryKey IS NOT NULL");
        b.Entity<Department>().HasIndex(x => new { x.RegionId, x.DirectoryCode }).IsUnique().HasFilter("DirectoryCode IS NOT NULL");
        b.Entity<MenuCategory>().HasIndex(x => new { x.RegionId, x.Name }).IsUnique();
        b.Entity<MenuItem>().HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MenuItem>().HasIndex(x => new { x.Date, x.Enabled });
        b.Entity<Order>().HasIndex(x => new { x.Email, x.Date }).IsUnique().HasFilter("Status = 'ACTIVO'");
        b.Entity<Order>().HasIndex(x => new { x.Date, x.DepartmentId, x.Status });
        b.Entity<Order>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<Holiday>().HasKey(x => new { x.RegionId, x.Date });
        b.Entity<Setting>().HasKey(x => new { x.RegionId, x.Key });
        b.Entity<Rating>().HasIndex(x => new { x.RegionId, x.Kind, x.Email, x.Scope }).IsUnique();
        b.Entity<Rating>().ToTable(t => t.HasCheckConstraint("CK_Rating_Score", "Score BETWEEN 1 AND 5"));
        b.Entity<RatingHistory>().HasIndex(x => new { x.PeriodId, x.Email });
        b.Entity<ImportRecord>().HasKey(x => x.Hash);
        b.Entity<SourceRecord>().HasIndex(x => new { x.RegionId, x.Sheet, x.Row }).IsUnique();
        b.Entity<JobRun>().HasKey(x => x.Key);
        b.Entity<MailMessageRecord>().HasIndex(x => new { x.SentAt, x.Attempts });
        Regional<AppUser>(b); Regional<Department>(b); Regional<MenuCategory>(b); Regional<MenuItem>(b);
        Regional<Order>(b); Regional<Holiday>(b); Regional<Setting>(b); Regional<Rating>(b);
        Regional<RatingHistory>(b); Regional<AuditEvent>(b); Regional<MailMessageRecord>(b);
        Regional<ImportRecord>(b); Regional<SourceRecord>(b); Regional<JobRun>(b);
        foreach (var entity in b.Model.GetEntityTypes())
        foreach (var property in entity.GetProperties().Where(p => p.ClrType == typeof(string)))
        {
            var size = property.Name switch { "Email" or "DirectoryAccount" => 254, "Date" => 10, "Name" => 150, "RegionId" => 32, "Key" => 100, "Kind" => 16, "Scope" or "PeriodId" or "Sheet" => 64, _ => 64 };
            if (property.IsKey() || property.IsForeignKey() || property.GetContainingIndexes().Any()) property.SetMaxLength(size);
        }
    }

    private void Regional<T>(ModelBuilder b) where T : class, IRegionalEntity
    {
        b.Entity<T>().Property(x => x.RegionId).HasMaxLength(32).HasDefaultValue(Region.LegacyId);
        b.Entity<T>().HasIndex(x => x.RegionId);
        b.Entity<T>().HasQueryFilter(x => x.RegionId == RegionId);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<IRegionalEntity>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (entry.State == EntityState.Added && entry.Entity.RegionId == Region.LegacyId) entry.Entity.RegionId = RegionId;
            if (!AllowCrossRegionWrites && (entry.Entity.RegionId != RegionId || entry.State != EntityState.Added && entry.Property(x => x.RegionId).OriginalValue != RegionId))
                throw new Services.BusinessException("Operación fuera de la regional seleccionada.");
        }
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State == EntityState.Modified))
        {
            if (entry.Entity is AppUser u) u.Revision = Guid.NewGuid().ToString();
            if (entry.Entity is Order o) o.Revision = Guid.NewGuid().ToString();
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
