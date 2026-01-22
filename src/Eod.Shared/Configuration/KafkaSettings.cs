namespace Eod.Shared.Configuration;

/// <summary>
/// Kafka configuration settings. Bind from appsettings.json section "Kafka".
/// </summary>
public sealed class KafkaSettings
{
    public const string SectionName = "Kafka";
    
    /// <summary>
    /// Kafka bootstrap servers (e.g., "localhost:9092" or "kafka:29092")
    /// </summary>
    public required string BootstrapServers { get; init; }
    
    /// <summary>
    /// Topic for raw trade messages from ingestion
    /// </summary>
    public string TradesTopic { get; init; } = "trades.raw";
    
    /// <summary>
    /// Dead letter queue topic for failed messages
    /// </summary>
    public string DlqTopic { get; init; } = "trades.dlq";
    
    /// <summary>
    /// Price updates topic for market data
    /// </summary>
    public string PricesTopic { get; init; } = "prices.updates";
    
    /// <summary>
    /// Consumer group ID (set per-service)
    /// </summary>
    public string? ConsumerGroupId { get; init; }
    
    /// <summary>
    /// Auto offset reset strategy: "earliest" or "latest"
    /// </summary>
    public string AutoOffsetReset { get; init; } = "earliest";
    
    /// <summary>
    /// Enable auto commit (set to false for manual commit control)
    /// </summary>
    public bool EnableAutoCommit { get; init; } = false;
    
    /// <summary>
    /// Max batch size for consuming
    /// </summary>
    public int MaxPollRecords { get; init; } = 500;
    
    /// <summary>
    /// Producer acks: "all", "1", "0"
    /// </summary>
    public string Acks { get; init; } = "all";
    
    /// <summary>
    /// Producer linger time in milliseconds (batching)
    /// </summary>
    public int LingerMs { get; init; } = 5;
    
    /// <summary>
    /// Enable idempotent producer (exactly-once semantics)
    /// </summary>
    public bool EnableIdempotence { get; init; } = true;
}
