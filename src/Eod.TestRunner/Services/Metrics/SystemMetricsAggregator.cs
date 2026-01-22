using Eod.TestRunner.Abstractions;
using Eod.TestRunner.Models.Metrics;
using Microsoft.Extensions.Logging;

namespace Eod.TestRunner.Services.Metrics;

/// <summary>
/// Aggregates metrics from all system components.
/// Follows DIP - depends on abstractions (IMetricsCollector) not concrete implementations.
/// </summary>
public sealed class SystemMetricsAggregator : ISystemMetricsAggregator
{
    private readonly IMetricsCollector<IngestionMetrics> _ingestionCollector;
    private readonly IMetricsCollector<KafkaMetrics> _kafkaCollector;
    private readonly IMetricsCollector<FlashPnlMetrics> _flashPnlCollector;
    private readonly IMetricsCollector<RegulatoryMetrics> _regulatoryCollector;
    private readonly IMetricsCollector<RedisMetrics> _redisCollector;
    private readonly IMetricsCollector<SqlServerMetrics> _sqlServerCollector;
    private readonly ILogger<SystemMetricsAggregator> _logger;
    
    public SystemMetricsAggregator(
        IMetricsCollector<IngestionMetrics> ingestionCollector,
        IMetricsCollector<KafkaMetrics> kafkaCollector,
        IMetricsCollector<FlashPnlMetrics> flashPnlCollector,
        IMetricsCollector<RegulatoryMetrics> regulatoryCollector,
        IMetricsCollector<RedisMetrics> redisCollector,
        IMetricsCollector<SqlServerMetrics> sqlServerCollector,
        ILogger<SystemMetricsAggregator> logger)
    {
        _ingestionCollector = ingestionCollector;
        _kafkaCollector = kafkaCollector;
        _flashPnlCollector = flashPnlCollector;
        _regulatoryCollector = regulatoryCollector;
        _redisCollector = redisCollector;
        _sqlServerCollector = sqlServerCollector;
        _logger = logger;
    }
    
    public async Task<SystemMetrics> CollectAllMetricsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Starting system metrics collection");
        
        // Collect all metrics in parallel for better performance
        var ingestionTask = _ingestionCollector.CollectAsync(cancellationToken);
        var kafkaTask = _kafkaCollector.CollectAsync(cancellationToken);
        var flashPnlTask = _flashPnlCollector.CollectAsync(cancellationToken);
        var regulatoryTask = _regulatoryCollector.CollectAsync(cancellationToken);
        var redisTask = _redisCollector.CollectAsync(cancellationToken);
        var sqlServerTask = _sqlServerCollector.CollectAsync(cancellationToken);
        
        await Task.WhenAll(
            ingestionTask, kafkaTask, flashPnlTask, 
            regulatoryTask, redisTask, sqlServerTask);
        
        _logger.LogDebug("System metrics collection completed");
        
        return new SystemMetrics
        {
            Ingestion = await ingestionTask,
            Kafka = await kafkaTask,
            FlashPnl = await flashPnlTask,
            Regulatory = await regulatoryTask,
            Redis = await redisTask,
            SqlServer = await sqlServerTask
        };
    }
}
