using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Eod.Shared.Data;

/// <summary>
/// Background service that ensures database migrations are applied on startup.
/// Uses EF Core migrations to create and update the database schema.
/// </summary>
public sealed class DatabaseMigrationService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DatabaseMigrationService> _logger;
    private readonly IHostApplicationLifetime _hostLifetime;

    public DatabaseMigrationService(
        IServiceProvider serviceProvider,
        ILogger<DatabaseMigrationService> logger,
        IHostApplicationLifetime hostLifetime)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _hostLifetime = hostLifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting database migration...");

        var maxRetries = 30;
        var retryDelay = TimeSpan.FromSeconds(2);

        for (var i = 0; i < maxRetries; i++)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<EodDbContext>();

                // Get connection string and ensure database exists first
                var connectionString = dbContext.Database.GetConnectionString();
                await EnsureDatabaseExistsAsync(connectionString!, stoppingToken);

                // Apply all pending migrations (creates tables, indexes, etc.)
                _logger.LogInformation("Checking for pending migrations...");
                var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync(stoppingToken);
                var pendingList = pendingMigrations.ToList();

                if (pendingList.Count > 0)
                {
                    _logger.LogInformation("Applying {Count} pending migrations: {Migrations}",
                        pendingList.Count, string.Join(", ", pendingList));
                    await dbContext.Database.MigrateAsync(stoppingToken);
                    _logger.LogInformation("All migrations applied successfully");
                }
                else
                {
                    _logger.LogInformation("Database is up to date, no pending migrations");
                }

                return; // Success, exit the service
            }
            catch (Exception ex) when (i < maxRetries - 1)
            {
                _logger.LogWarning(ex, "Database migration attempt {Attempt}/{MaxRetries} failed. Retrying in {Delay}s...",
                    i + 1, maxRetries, retryDelay.TotalSeconds);
                await Task.Delay(retryDelay, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database migration failed after {MaxRetries} attempts. Shutting down...", maxRetries);
                _hostLifetime.StopApplication();
                throw;
            }
        }
    }

    /// <summary>
    /// Ensures the database exists by creating it if necessary.
    /// This uses the master database to check/create the target database.
    /// </summary>
    private async Task EnsureDatabaseExistsAsync(string connectionString, CancellationToken stoppingToken)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var databaseName = builder.InitialCatalog;
        builder.InitialCatalog = "master";

        _logger.LogInformation("Ensuring database '{Database}' exists...", databaseName);

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(stoppingToken);

        var checkDbSql = $"SELECT COUNT(*) FROM sys.databases WHERE name = @dbName";
        await using var checkCmd = new SqlCommand(checkDbSql, connection);
        checkCmd.Parameters.AddWithValue("@dbName", databaseName);
        var exists = (int)await checkCmd.ExecuteScalarAsync(stoppingToken)! > 0;

        if (!exists)
        {
            _logger.LogInformation("Creating database '{Database}'...", databaseName);
            var createDbSql = $"CREATE DATABASE [{databaseName}]";
            await using var createCmd = new SqlCommand(createDbSql, connection);
            await createCmd.ExecuteNonQueryAsync(stoppingToken);
            _logger.LogInformation("Database '{Database}' created successfully", databaseName);
        }
        else
        {
            _logger.LogInformation("Database '{Database}' already exists", databaseName);
        }
    }
}
