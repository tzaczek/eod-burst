using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Eod.Shared.Data;

/// <summary>
/// Design-time factory for EodDbContext.
/// Used by EF Core tools for migrations.
/// </summary>
public class EodDbContextFactory : IDesignTimeDbContextFactory<EodDbContext>
{
    public EodDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<EodDbContext>();
        
        // Connection string for migrations (development)
        // Must be set via environment variable - no hardcoded credentials
        var connectionString = Environment.GetEnvironmentVariable("EOD_CONNECTION_STRING");
        
        if (string.IsNullOrEmpty(connectionString))
        {
            // Build from individual environment variables
            var password = Environment.GetEnvironmentVariable("MSSQL_SA_PASSWORD") ?? "YourStrong@Passw0rd";
            connectionString = $"Server=localhost,1433;Database=EodTrades;User Id=sa;Password={password};TrustServerCertificate=true;";
        }
        
        optionsBuilder.UseSqlServer(connectionString);
        
        return new EodDbContext(optionsBuilder.Options);
    }
}
