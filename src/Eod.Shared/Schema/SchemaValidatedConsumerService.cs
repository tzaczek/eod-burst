using Confluent.Kafka;
using Eod.Shared.Configuration;
using Eod.Shared.Kafka;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eod.Shared.Schema;

/// <summary>
/// Kafka consumer with Schema Registry integration for Protobuf messages.
/// Handles both schema-prefixed and raw Protobuf messages.
/// </summary>
public sealed class SchemaValidatedConsumerService
{
    private readonly KafkaConsumerService _consumer;
    private readonly SchemaRegistryService _schemaRegistry;
    private readonly SchemaRegistrySettings _settings;
    private readonly ILogger<SchemaValidatedConsumerService> _logger;
    
    private long _messagesWithSchema;
    private long _messagesWithoutSchema;
    private long _schemaValidationErrors;

    public SchemaValidatedConsumerService(
        KafkaConsumerService consumer,
        SchemaRegistryService schemaRegistry,
        IOptions<SchemaRegistrySettings> settings,
        ILogger<SchemaValidatedConsumerService> logger)
    {
        _consumer = consumer;
        _schemaRegistry = schemaRegistry;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Consumes and deserializes Protobuf messages from Kafka.
    /// Handles both schema-prefixed (Confluent wire format) and raw Protobuf messages.
    /// </summary>
    /// <typeparam name="T">Protobuf message type</typeparam>
    /// <returns>Async enumerable of deserialized messages with metadata</returns>
    public async IAsyncEnumerable<SchemaValidatedMessage<T>> ConsumeAsync<T>(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) 
        where T : IMessage<T>, new()
    {
        await foreach (var result in _consumer.ConsumeAsync(cancellationToken))
        {
            T? message = default;
            int? schemaId = null;
            string? validationError = null;

            try
            {
                (message, schemaId) = DeserializeMessage<T>(result.Message.Value);
                
                if (schemaId.HasValue)
                {
                    Interlocked.Increment(ref _messagesWithSchema);
                }
                else
                {
                    Interlocked.Increment(ref _messagesWithoutSchema);
                }
            }
            catch (InvalidProtocolBufferException ex)
            {
                Interlocked.Increment(ref _schemaValidationErrors);
                validationError = $"Protobuf deserialization failed: {ex.Message}";
                _logger.LogWarning(ex, 
                    "Failed to deserialize message from {Topic}-{Partition}@{Offset}",
                    result.Topic, result.Partition, result.Offset);
            }
            catch (SchemaValidationException ex)
            {
                Interlocked.Increment(ref _schemaValidationErrors);
                validationError = ex.Message;
                _logger.LogWarning(ex,
                    "Schema validation failed for message from {Topic}-{Partition}@{Offset}",
                    result.Topic, result.Partition, result.Offset);
            }

            yield return new SchemaValidatedMessage<T>
            {
                Message = message,
                SchemaId = schemaId,
                Key = result.Message.Key,
                Topic = result.Topic,
                Partition = result.Partition.Value,
                Offset = result.Offset.Value,
                Timestamp = result.Message.Timestamp.UtcDateTime,
                RawResult = result,
                ValidationError = validationError
            };
        }
    }

    /// <summary>
    /// Deserializes a Protobuf message, handling both schema-prefixed and raw formats.
    /// </summary>
    public (T? Message, int? SchemaId) DeserializeMessage<T>(byte[] payload) where T : IMessage<T>, new()
    {
        if (payload == null || payload.Length == 0)
        {
            return (default, null);
        }

        // Check for Confluent Schema Registry wire format
        // Format: [0] magic byte (0), [1-4] schema ID (big-endian), [5] message index, [6+] payload
        if (_settings.Enabled && payload.Length > 6 && payload[0] == 0)
        {
            try
            {
                // Extract schema ID (big-endian)
                var schemaId = (payload[1] << 24) | (payload[2] << 16) | (payload[3] << 8) | payload[4];
                
                // Skip message index (varint, typically just 0 = 1 byte for single message files)
                var payloadStart = 5;
                var messageIndex = payload[5];
                payloadStart++; // Skip the message index byte
                
                // For multi-byte varints (rare), we'd need to parse them properly
                // For now, assume single-byte (covers 99% of cases)
                
                // Deserialize the actual protobuf payload
                var actualPayload = new byte[payload.Length - payloadStart];
                Array.Copy(payload, payloadStart, actualPayload, 0, actualPayload.Length);
                
                var parser = new MessageParser<T>(() => new T());
                var message = parser.ParseFrom(actualPayload);
                
                return (message, schemaId);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse as schema-prefixed message, trying raw Protobuf");
                // Fall through to try raw protobuf parsing
            }
        }

        // Try parsing as raw Protobuf (no schema prefix)
        {
            var parser = new MessageParser<T>(() => new T());
            var message = parser.ParseFrom(payload);
            return (message, null);
        }
    }

    /// <summary>
    /// Validates that a message payload matches the expected schema.
    /// </summary>
    public async Task<bool> ValidateMessageSchemaAsync(
        byte[] payload,
        string topic,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled || payload == null || payload.Length < 6 || payload[0] != 0)
        {
            return true; // No schema validation needed/possible
        }

        var schemaId = (payload[1] << 24) | (payload[2] << 16) | (payload[3] << 8) | payload[4];
        
        // Verify schema exists in registry
        var schema = await _schemaRegistry.GetSchemaByIdAsync(schemaId, cancellationToken);
        return schema != null;
    }

    /// <summary>
    /// Commits the offset for a consumed message.
    /// </summary>
    public void Commit(ConsumeResult<string, byte[]> result) => _consumer.Commit(result);

    /// <summary>
    /// Gets consumer metrics including schema validation stats.
    /// </summary>
    public SchemaConsumerMetrics GetMetrics() => new()
    {
        MessagesWithSchema = Interlocked.Read(ref _messagesWithSchema),
        MessagesWithoutSchema = Interlocked.Read(ref _messagesWithoutSchema),
        SchemaValidationErrors = Interlocked.Read(ref _schemaValidationErrors),
        TotalLag = _consumer.GetTotalLag()
    };
}

/// <summary>
/// A consumed message with schema validation metadata.
/// </summary>
public sealed class SchemaValidatedMessage<T> where T : IMessage<T>
{
    /// <summary>
    /// The deserialized Protobuf message (null if deserialization failed).
    /// </summary>
    public T? Message { get; init; }
    
    /// <summary>
    /// The schema ID from the message (null if not schema-prefixed).
    /// </summary>
    public int? SchemaId { get; init; }
    
    /// <summary>
    /// The message key.
    /// </summary>
    public required string Key { get; init; }
    
    /// <summary>
    /// The Kafka topic.
    /// </summary>
    public required string Topic { get; init; }
    
    /// <summary>
    /// The partition number.
    /// </summary>
    public int Partition { get; init; }
    
    /// <summary>
    /// The offset within the partition.
    /// </summary>
    public long Offset { get; init; }
    
    /// <summary>
    /// The message timestamp.
    /// </summary>
    public DateTime Timestamp { get; init; }
    
    /// <summary>
    /// The raw Kafka consume result (for committing).
    /// </summary>
    public required ConsumeResult<string, byte[]> RawResult { get; init; }
    
    /// <summary>
    /// Validation error message (null if validation passed).
    /// </summary>
    public string? ValidationError { get; init; }
    
    /// <summary>
    /// Whether the message was successfully deserialized and validated.
    /// </summary>
    public bool IsValid => Message != null && ValidationError == null;
}

/// <summary>
/// Schema consumer metrics.
/// </summary>
public sealed class SchemaConsumerMetrics
{
    public long MessagesWithSchema { get; init; }
    public long MessagesWithoutSchema { get; init; }
    public long SchemaValidationErrors { get; init; }
    public long TotalLag { get; init; }
}

/// <summary>
/// Exception thrown when schema validation fails.
/// </summary>
public sealed class SchemaValidationException : Exception
{
    public int? SchemaId { get; }
    
    public SchemaValidationException(string message, int? schemaId = null) 
        : base(message)
    {
        SchemaId = schemaId;
    }
    
    public SchemaValidationException(string message, Exception innerException, int? schemaId = null)
        : base(message, innerException)
    {
        SchemaId = schemaId;
    }
}
