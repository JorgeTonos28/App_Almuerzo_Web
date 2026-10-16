using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Almuerzo.Web.Data;

public class SqlServerAppDbContext(DbContextOptions<SqlServerAppDbContext> options) : AppDbContext(options);

// Separate migration histories/snapshots for each provider, with no live connection needed to scaffold.
public class SqlServerDesignFactory : IDesignTimeDbContextFactory<SqlServerAppDbContext>
{
    public SqlServerAppDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<SqlServerAppDbContext>()
        .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=AlmuerzoDesign;Integrated Security=true;Encrypt=true;TrustServerCertificate=false").Options);
}
public class SqliteDesignFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=design.db").Options);
}
