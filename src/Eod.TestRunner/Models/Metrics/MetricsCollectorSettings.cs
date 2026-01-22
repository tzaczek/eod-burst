namespace Eod.TestRunner.Models.Metrics;

/// <summary>
/// Configuration settings for metrics collectors.
/// </summary>
public sealed class MetricsCollectorSettings
{
    public const string SectionName = "Metrics";
    
    /// <summary>
    /// URL of the Kafka Exporter for Prometheus metrics
    /// </summary>
    public string KafkaExporterUrl { get; init; } = "http://kafka-exporter:9308";
    
    /// <summary>
    /// URL of the Redis Exporter for Prometheus metrics
    /// </summary>
    public string RedisExporterUrl { get; init; } = "http://redis-exporter:9121";
}
