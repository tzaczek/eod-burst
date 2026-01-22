using Confluent.Kafka;
using Eod.Shared.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eod.Shared.Kafka;

/// <summary>
/// High-performance Kafka producer with batching and delivery reports.
/// </summary>
public sealed class KafkaProducerService : IDisposable
{
    private readonly IProducer<string, byte[]> _producer;
    private readonly ILogger<KafkaProducerService> _logger;
    private readonly KafkaSettings _settings;
    private long _messagesSent;
    private long _deliveryErrors;

    public KafkaProducerService(
        IOptions<KafkaSettings> settings,
        ILogger<KafkaProducerService> logger)
    {
        _settings = settings.Value;
        _logger = logger;

        var config = new ProducerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            Acks = ParseAcks(_settings.Acks),
            LingerMs = _settings.LingerMs,
            EnableIdempotence = _settings.EnableIdempotence,
            
            // Performance tuning
            BatchSize = 65536,  // 64KB batches
            CompressionType = CompressionType.Lz4,
            MessageMaxBytes = 1048576,  // 1MB max message
            
            // Reliability
            MessageSendMaxRetries = 3,
            RetryBackoffMs = 100,
            RequestTimeoutMs = 30000
        };

        _producer = new ProducerBuilder<string, byte[]>(config)
            .SetErrorHandler(OnError)
            .SetLogHandler(OnLog)
            .Build();

        _logger.LogInformation(
            "Kafka producer initialized. Bootstrap: {Servers}, Acks: {Acks}",
            _settings.BootstrapServers,
            _settings.Acks);
    }

    /// <summary>
    /// Produces a message to the specified topic with the given key.
    /// </summary>
    public async Task<DeliveryResult<string, byte[]>> ProduceAsync(
        string topic,
        string key,
        byte[] value,
        CancellationToken cancellationToken = default)
    {
        var message = new Message<string, byte[]>
        {
            Key = key,
            Value = value,
            Timestamp = new Timestamp(DateTime.UtcNow)
        };

        try
        {
            var result = await _producer.ProduceAsync(topic, message, cancellationToken);
            Interlocked.Increment(ref _messagesSent);
            return result;
        }
        catch (ProduceException<string, byte[]> ex)
        {
            Interlocked.Increment(ref _deliveryErrors);
            _logger.LogError(ex, 
                "Failed to produce message to {Topic}. Key: {Key}, Error: {Error}",
                topic, key, ex.Error.Reason);
            throw;
        }
    }

    /// <summary>
    /// Produces a message without waiting for acknowledgment (fire-and-forget).
    /// Use delivery handler for async error handling.
    /// </summary>
    public void Produce(
        string topic,
        string key,
        byte[] value,
        Action<DeliveryReport<string, byte[]>>? deliveryHandler = null)
    {
        var message = new Message<string, byte[]>
        {
            Key = key,
            Value = value,
            Timestamp = new Timestamp(DateTime.UtcNow)
        };

        _producer.Produce(topic, message, deliveryHandler ?? DefaultDeliveryHandler);
    }

    /// <summary>
    /// Flushes pending messages. Call before shutdown.
    /// </summary>
    public void Flush(TimeSpan timeout)
    {
        _producer.Flush(timeout);
    }

    public long MessagesSent => Interlocked.Read(ref _messagesSent);
    public long DeliveryErrors => Interlocked.Read(ref _deliveryErrors);

    private void DefaultDeliveryHandler(DeliveryReport<string, byte[]> report)
    {
        if (report.Error.IsError)
        {
            Interlocked.Increment(ref _deliveryErrors);
            _logger.LogWarning(
                "Delivery failed for {Topic}/{Partition}. Error: {Error}",
                report.Topic, report.Partition, report.Error.Reason);
        }
        else
        {
            Interlocked.Increment(ref _messagesSent);
        }
    }

    private void OnError(IProducer<string, byte[]> producer, Error error)
    {
        _logger.LogError("Kafka producer error: {Error}", error.Reason);
    }

    private void OnLog(IProducer<string, byte[]> producer, LogMessage log)
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

    private static Acks ParseAcks(string acks) => acks.ToLowerInvariant() switch
    {
        "all" or "-1" => Confluent.Kafka.Acks.All,
        "1" => Confluent.Kafka.Acks.Leader,
        "0" => Confluent.Kafka.Acks.None,
        _ => Confluent.Kafka.Acks.All
    };

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(10));
        _producer.Dispose();
    }
}
