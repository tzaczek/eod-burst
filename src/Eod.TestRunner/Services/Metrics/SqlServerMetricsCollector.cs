using Eod.Shared.Configuration;
using Eod.TestRunner.Abstractions;
using Eod.TestRunner.Models.Metrics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eod.TestRunner.Services.Metrics;

/// <summary>
/// Collects metrics from SQL Server.
/// Follows SRP - only responsible for collecting SQL Server metrics.
/// </summary>
public sealed class SqlServerMetricsCollector : IMetricsCollector<SqlServerMetrics>, IHealthCheckable
{
    private readonly SqlServerSettings _settings;
    private readonly ILogger<SqlServerMetricsCollector> _logger;
    
    public SqlServerMetricsCollector(
        IOptions<SqlServerSettings> settings,
        ILogger<SqlServerMetricsCollector> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }
    
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var conn = new SqlConnection(_settings.ConnectionString);
            await conn.OpenAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Health check failed for SQL Server");
            return false;
        }
    }
    
    public async Task<SqlServerMetrics> CollectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var conn = new SqlConnection(_settings.ConnectionString);
            await conn.OpenAsync(cancellationToken);
            
            await using var cmd = new SqlCommand("SELECT COUNT(*) FROM dbo.Trades", conn);
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            var totalTrades = result != null ? Convert.ToInt64(result) : 0;
            
            return new SqlServerMetrics
            {
                TotalTrades = totalTrades,
                Status = "up"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to collect SQL Server metrics");
            return new SqlServerMetrics { Status = "down" };
        }
    }
}
