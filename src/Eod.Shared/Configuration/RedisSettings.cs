namespace Eod.Shared.Configuration;

/// <summary>
/// Redis configuration settings. Bind from appsettings.json section "Redis".
/// </summary>
public sealed class RedisSettings
{
    public const string SectionName = "Redis";
    
    /// <summary>
    /// Redis connection string (e.g., "localhost:6379" or "redis:6379,abortConnect=false")
    /// </summary>
    public required string ConnectionString { get; init; }
    
    /// <summary>
    /// Key prefix for position hashes
    /// </summary>
    public string PositionKeyPrefix { get; init; } = "positions";
    
    /// <summary>
    /// Key prefix for P&L data
    /// </summary>
    public string PnlKeyPrefix { get; init; } = "pnl";
    
    /// <summary>
    /// Pub/Sub channel for P&L updates
    /// </summary>
    public string PnlUpdateChannel { get; init; } = "pnl-updates";
    
    /// <summary>
    /// Key prefix for price cache
    /// </summary>
    public string PriceKeyPrefix { get; init; } = "prices";
    
    /// <summary>
    /// Connection timeout in milliseconds
    /// </summary>
    public int ConnectTimeoutMs { get; init; } = 5000;
    
    /// <summary>
    /// Sync timeout in milliseconds
    /// </summary>
    public int SyncTimeoutMs { get; init; } = 1000;
    
    /// <summary>
    /// Number of retry attempts
    /// </summary>
    public int ConnectRetry { get; init; } = 3;
}
