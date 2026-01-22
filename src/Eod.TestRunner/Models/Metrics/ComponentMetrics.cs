namespace Eod.TestRunner.Models.Metrics;

/// <summary>
/// Base class for component metrics following Open/Closed Principle.
/// Can be extended without modification.
/// </summary>
public abstract record ComponentMetrics
{
    public string Status { get; init; } = "down";
    public bool IsHealthy => Status == "up";
}

/// <summary>
/// Metrics for the Ingestion service.
/// </summary>
public sealed record IngestionMetrics : ComponentMetrics
{
    public long TradesIngested { get; init; }
    public int MessagesPerSecond { get; init; }
}

/// <summary>
/// Metrics for Kafka messaging system.
/// </summary>
public sealed record KafkaMetrics : ComponentMetrics
{
    public long MessagesInTopic { get; init; }
    public long ConsumerLag { get; init; }
}

/// <summary>
/// Metrics for Flash P&L service.
/// </summary>
public sealed record FlashPnlMetrics : ComponentMetrics
{
    public long TradesProcessed { get; init; }
    public long PositionsInRedis { get; init; }
}

/// <summary>
/// Metrics for Regulatory service.
/// </summary>
public sealed record RegulatoryMetrics : ComponentMetrics
{
    public long TradesInserted { get; init; }
    public int BatchesPending { get; init; }
}

/// <summary>
/// Metrics for Redis cache.
/// </summary>
public sealed record RedisMetrics : ComponentMetrics
{
    public int ConnectedClients { get; init; }
    public long KeysCount { get; init; }
}

/// <summary>
/// Metrics for SQL Server database.
/// </summary>
public sealed record SqlServerMetrics : ComponentMetrics
{
    public long TotalTrades { get; init; }
}

/// <summary>
/// Aggregated system metrics for the architecture diagram.
/// </summary>
public sealed record SystemMetrics
{
    public required IngestionMetrics Ingestion { get; init; }
    public required KafkaMetrics Kafka { get; init; }
    public required FlashPnlMetrics FlashPnl { get; init; }
    public required RegulatoryMetrics Regulatory { get; init; }
    public required RedisMetrics Redis { get; init; }
    public required SqlServerMetrics SqlServer { get; init; }
}
