using System.Runtime.CompilerServices;
using Confluent.Kafka;
using Eod.Shared.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eod.Shared.Kafka;

/// <summary>
/// High-performance Kafka consumer with manual commit control.
/// </summary>
public sealed class KafkaConsumerService : IDisposable
{
    private readonly IConsumer<string, byte[]> _consumer;
    private readonly ILogger<KafkaConsumerService> _logger;
    private readonly KafkaSettings _settings;
    private long _messagesConsumed;
    private long _consumeErrors;

    public KafkaConsumerService(
        IOptions<KafkaSettings> settings,
        ILogger<KafkaConsumerService> logger)
    {
        _settings = settings.Value;
        _logger = logger;

        if (string.IsNullOrEmpty(_settings.ConsumerGroupId))
        {
            throw new InvalidOperationException("ConsumerGroupId must be configured for Kafka consumer");
        }

        var config = new ConsumerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            GroupId = _settings.ConsumerGroupId,
            AutoOffsetReset = ParseAutoOffsetReset(_settings.AutoOffsetReset),
            EnableAutoCommit = _settings.EnableAutoCommit,
            
            // Performance tuning
            MaxPollIntervalMs = 300000,  // 5 minutes max between polls
            SessionTimeoutMs = 45000,
            FetchMinBytes = 1,
            FetchMaxBytes = 52428800,  // 50MB
            MaxPartitionFetchBytes = 1048576,  // 1MB per partition
            
            // Exactly-once support
            IsolationLevel = IsolationLevel.ReadCommitted,
            EnableAutoOffsetStore = false
        };

        _consumer = new ConsumerBuilder<string, byte[]>(config)
            .SetErrorHandler(OnError)
            .SetLogHandler(OnLog)
            .SetPartitionsAssignedHandler(OnPartitionsAssigned)
            .SetPartitionsRevokedHandler(OnPartitionsRevoked)
            .Build();

        _logger.LogInformation(
            "Kafka consumer initialized. Bootstrap: {Servers}, Group: {Group}",
            _settings.BootstrapServers,
            _settings.ConsumerGroupId);
    }

    /// <summary>
    /// Subscribes to the trades topic.
    /// </summary>
    public void Subscribe(string? topic = null)
    {
        var targetTopic = topic ?? _settings.TradesTopic;
        _consumer.Subscribe(targetTopic);
        _logger.LogInformation("Subscribed to topic: {Topic}", targetTopic);
    }

    /// <summary>
    /// Async enumerable for consuming messages. Automatically handles polling.
    /// </summary>
    public async IAsyncEnumerable<ConsumeResult<string, byte[]>> ConsumeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ConsumeResult<string, byte[]>? result = null;

            try
            {
                // Poll with timeout to allow cancellation check
                result = _consumer.Consume(TimeSpan.FromMilliseconds(100));
            }
            catch (ConsumeException ex)
            {
                Interlocked.Increment(ref _consumeErrors);
                _logger.LogError(ex,
                    "Error consuming message: {Error}",
                    ex.Error.Reason);
                continue;
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (result == null || result.IsPartitionEOF)
            {
                // No message available, yield to allow other tasks
                await Task.Yield();
                continue;
            }

            Interlocked.Increment(ref _messagesConsumed);
            yield return result;
        }
    }

    /// <summary>
    /// Commits the specified message offset.
    /// </summary>
    public void Commit(ConsumeResult<string, byte[]> result)
    {
        try
        {
            _consumer.StoreOffset(result);
            _consumer.Commit();
        }
        catch (KafkaException ex)
        {
            _logger.LogWarning(ex, "Failed to commit offset: {Error}", ex.Error.Reason);
        }
    }

    /// <summary>
    /// Commits all stored offsets.
    /// </summary>
    public void CommitAll()
    {
        try
        {
            _consumer.Commit();
        }
        catch (KafkaException ex)
        {
            _logger.LogWarning(ex, "Failed to commit offsets: {Error}", ex.Error.Reason);
        }
    }

    /// <summary>
    /// Gets current consumer lag across all assigned partitions.
    /// </summary>
    public long GetTotalLag()
    {
        long totalLag = 0;
        var assignment = _consumer.Assignment;
        
        foreach (var tp in assignment)
        {
            try
            {
                var position = _consumer.Position(tp);
                var watermarks = _consumer.QueryWatermarkOffsets(tp, TimeSpan.FromSeconds(5));
                
                if (position != Offset.Unset && watermarks.High != Offset.Unset)
                {
                    totalLag += watermarks.High.Value - position.Value;
                }
            }
            catch (KafkaException ex)
            {
                _logger.LogWarning(ex, "Failed to get lag for {TopicPartition}", tp);
            }
        }
        
        return totalLag;
    }

    public long MessagesConsumed => Interlocked.Read(ref _messagesConsumed);
    public long ConsumeErrors => Interlocked.Read(ref _consumeErrors);

    private void OnError(IConsumer<string, byte[]> consumer, Error error)
    {
        _logger.LogError("Kafka consumer error: {Error}", error.Reason);
    }

    private void OnLog(IConsumer<string, byte[]> consumer, LogMessage log)
    {
        var level = log.Level switch
        {
            SyslogLevel.Emergency or SyslogLevel.Alert or SyslogLevel.Critical => LogLevel.Critical,
            SyslogLevel.Error => LogLevel.Error,
            SyslogLevel.Warning or SyslogLevel.Notice => LogLevel.Warning,
            SyslogLevel.Info => LogLevel.Information,
            _ => LogLevel.Debug
        };

        _logger.Log(level, "Kafka: {Message}", log.Message);
    }

    private void OnPartitionsAssigned(IConsumer<string, byte[]> consumer, List<TopicPartition> partitions)
    {
        _logger.LogInformation(
            "Partitions assigned: {Partitions}",
            string.Join(", ", partitions.Select(p => $"{p.Topic}[{p.Partition}]")));
    }

    private void OnPartitionsRevoked(IConsumer<string, byte[]> consumer, List<TopicPartitionOffset> partitions)
    {
        _logger.LogInformation(
            "Partitions revoked: {Partitions}",
            string.Join(", ", partitions.Select(p => $"{p.Topic}[{p.Partition}]@{p.Offset}")));
    }

    private static AutoOffsetReset ParseAutoOffsetReset(string value) => value.ToLowerInvariant() switch
    {
        "earliest" => Confluent.Kafka.AutoOffsetReset.Earliest,
        "latest" => Confluent.Kafka.AutoOffsetReset.Latest,
        _ => Confluent.Kafka.AutoOffsetReset.Earliest
    };

    public void Dispose()
    {
        _consumer.Close();
        _consumer.Dispose();
    }
}
