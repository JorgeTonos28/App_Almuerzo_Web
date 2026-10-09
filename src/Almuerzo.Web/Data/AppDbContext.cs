using Almuerzo.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Almuerzo.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
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

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppUser>().HasKey(x => x.Email);
        b.Entity<AppUser>().HasIndex(x => x.EmployeeCode).IsUnique().HasFilter("EmployeeCode IS NOT NULL");
        b.Entity<AppUser>().HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AppUser>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<Department>().HasIndex(x => x.Name).IsUnique();
        b.Entity<MenuCategory>().HasIndex(x => x.Name).IsUnique();
        b.Entity<MenuItem>().HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MenuItem>().HasIndex(x => new { x.Date, x.Enabled });
        b.Entity<Order>().HasIndex(x => new { x.Email, x.Date }).IsUnique().HasFilter("Status = 'ACTIVO'");
        b.Entity<Order>().HasIndex(x => new { x.Date, x.DepartmentId, x.Status });
        b.Entity<Order>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<Holiday>().HasKey(x => x.Date);
        b.Entity<Setting>().HasKey(x => x.Key);
        b.Entity<Rating>().HasIndex(x => new { x.Kind, x.Email, x.Scope }).IsUnique();
        b.Entity<Rating>().ToTable(t => t.HasCheckConstraint("CK_Rating_Score", "Score BETWEEN 1 AND 5"));
        b.Entity<RatingHistory>().HasIndex(x => new { x.PeriodId, x.Email });
        b.Entity<ImportRecord>().HasKey(x => x.Hash);
        b.Entity<SourceRecord>().HasIndex(x => new { x.Sheet, x.Row }).IsUnique();
        b.Entity<JobRun>().HasKey(x => x.Key);
        b.Entity<MailMessageRecord>().HasIndex(x => new { x.SentAt, x.Attempts });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State == EntityState.Modified))
        {
            if (entry.Entity is AppUser u) u.Revision = Guid.NewGuid().ToString();
            if (entry.Entity is Order o) o.Revision = Guid.NewGuid().ToString();
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
