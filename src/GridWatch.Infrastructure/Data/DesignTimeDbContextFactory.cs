using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GridWatch.Infrastructure.Data;

/// <summary>
/// Used only by the EF Core tools (dotnet ef migrations add ...). Migrations target SQL Server,
/// the production database; the connection string is never opened when generating a migration.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<GridWatchDbContext>
{
    public GridWatchDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<GridWatchDbContext>()
            .UseSqlServer("Server=localhost;Database=GridWatch;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);
}
