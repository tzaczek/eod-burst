using Confluent.Kafka;
using Eod.Shared.Configuration;
using Eod.Shared.Kafka;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eod.Shared.Schema;

/// <summary>
/// Kafka producer with Schema Registry integration for Protobuf messages.
/// Validates and registers schemas before producing messages.
/// </summary>
public sealed class SchemaValidatedProducerService : IDisposable
{
    private readonly KafkaProducerService _producer;
    private readonly SchemaRegistryService _schemaRegistry;
    private readonly SchemaRegistrySettings _settings;
    private readonly ILogger<SchemaValidatedProducerService> _logger;
    
    // Cache of registered schema IDs per topic
    private readonly Dictionary<string, int> _registeredSchemas = new();
    private readonly SemaphoreSlim _registrationLock = new(1, 1);

    public SchemaValidatedProducerService(
        KafkaProducerService producer,
        SchemaRegistryService schemaRegistry,
        IOptions<SchemaRegistrySettings> settings,
        ILogger<SchemaValidatedProducerService> logger)
    {
        _producer = producer;
        _schemaRegistry = schemaRegistry;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Produces a Protobuf message with schema validation.
    /// </summary>
    /// <typeparam name="T">Protobuf message type</typeparam>
    /// <param name="topic">Kafka topic</param>
    /// <param name="key">Message key</param>
    /// <param name="message">Protobuf message</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Delivery result</returns>
    public async Task<DeliveryResult<string, byte[]>> ProduceAsync<T>(
        string topic,
        string key,
        T message,
        CancellationToken cancellationToken = default) where T : IMessage<T>
    {
        if (_settings.Enabled && _settings.AutoRegisterSchemas)
        {
            await EnsureSchemaRegisteredAsync<T>(topic, cancellationToken);
        }

        // Serialize with schema ID prefix (Confluent wire format)
        var payload = SerializeWithSchemaId(message, topic);
        
        return await _producer.ProduceAsync(topic, key, payload, cancellationToken);
    }

    /// <summary>
    /// Produces a Protobuf message without waiting for acknowledgment.
    /// </summary>
    public void Produce<T>(
        string topic,
        string key,
        T message,
        Action<DeliveryReport<string, byte[]>>? deliveryHandler = null) where T : IMessage<T>
    {
        if (_settings.Enabled && _settings.AutoRegisterSchemas)
        {
            // For fire-and-forget, we need to ensure schema is registered synchronously
            EnsureSchemaRegisteredAsync<T>(topic, CancellationToken.None).GetAwaiter().GetResult();
        }

        var payload = SerializeWithSchemaId(message, topic);
        _producer.Produce(topic, key, payload, deliveryHandler);
    }

    /// <summary>
    /// Validates that a message schema is compatible before producing.
    /// </summary>
    public async Task<CompatibilityResult> ValidateSchemaAsync<T>(
        string topic,
        CancellationToken cancellationToken = default) where T : IMessage<T>, new()
    {
        if (!_settings.Enabled)
        {
            return new CompatibilityResult { IsCompatible = true, Message = "Schema validation disabled" };
        }

        var descriptor = new T().Descriptor.File;
        return await _schemaRegistry.CheckCompatibilityAsync(topic, descriptor, false, cancellationToken);
    }

    /// <summary>
    /// Gets the schema ID for a message type on a topic.
    /// </summary>
    public async Task<int> GetSchemaIdAsync<T>(
        string topic,
        CancellationToken cancellationToken = default) where T : IMessage<T>, new()
    {
        var cacheKey = GetCacheKey<T>(topic);
        
        if (_registeredSchemas.TryGetValue(cacheKey, out var schemaId))
        {
            return schemaId;
        }

        await EnsureSchemaRegisteredAsync<T>(topic, cancellationToken);
        return _registeredSchemas.GetValueOrDefault(cacheKey, -1);
    }

    private async Task EnsureSchemaRegisteredAsync<T>(
        string topic,
        CancellationToken cancellationToken) where T : IMessage<T>
    {
        var cacheKey = GetCacheKey<T>(topic);
        
        if (_registeredSchemas.ContainsKey(cacheKey))
        {
            return;
        }

        await _registrationLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_registeredSchemas.ContainsKey(cacheKey))
            {
                return;
            }

            var descriptor = GetFileDescriptor<T>();
            var schemaId = await _schemaRegistry.RegisterSchemaAsync(topic, descriptor, false, cancellationToken);
            
            if (schemaId >= 0)
            {
                _registeredSchemas[cacheKey] = schemaId;
                _logger.LogDebug(
                    "Schema registered for {MessageType} on topic {Topic}. SchemaId: {SchemaId}",
                    typeof(T).Name, topic, schemaId);
            }
        }
        finally
        {
            _registrationLock.Release();
        }
    }

    private byte[] SerializeWithSchemaId<T>(T message, string topic) where T : IMessage<T>
    {
        var payload = message.ToByteArray();
        
        if (!_settings.Enabled)
        {
            return payload;
        }

        var cacheKey = GetCacheKey<T>(topic);
        
        if (!_registeredSchemas.TryGetValue(cacheKey, out var schemaId) || schemaId < 0)
        {
            // No schema ID available, return plain payload
            return payload;
        }

        // Confluent Schema Registry wire format:
        // [0] Magic byte (0)
        // [1-4] Schema ID (big-endian)
        // [5+] Protobuf payload with message index array
        var result = new byte[5 + 1 + payload.Length]; // +1 for message index (single message = 0)
        
        // Magic byte
        result[0] = 0;
        
        // Schema ID (big-endian)
        result[1] = (byte)(schemaId >> 24);
        result[2] = (byte)(schemaId >> 16);
        result[3] = (byte)(schemaId >> 8);
        result[4] = (byte)schemaId;
        
        // Message index array (varint 0 for first message in file)
        result[5] = 0;
        
        // Payload
        Array.Copy(payload, 0, result, 6, payload.Length);
        
        return result;
    }

    private static FileDescriptor GetFileDescriptor<T>() where T : IMessage<T>
    {
        // Get the descriptor through reflection
        var property = typeof(T).GetProperty("Descriptor", 
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        
        if (property?.GetValue(null) is MessageDescriptor messageDescriptor)
        {
            return messageDescriptor.File;
        }

        throw new InvalidOperationException($"Cannot get FileDescriptor for type {typeof(T).Name}");
    }

    private static string GetCacheKey<T>(string topic) => $"{topic}:{typeof(T).FullName}";

    public void Flush(TimeSpan timeout) => _producer.Flush(timeout);

    public void Dispose()
    {
        _registrationLock.Dispose();
    }
}
