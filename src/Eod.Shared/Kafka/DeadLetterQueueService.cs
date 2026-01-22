using System.Diagnostics;
using Confluent.Kafka;
using Eod.Shared.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eod.Shared.Kafka;

/// <summary>
/// Service for handling Dead Letter Queue operations.
/// Provides methods to send failed messages to DLQ with error context.
/// Tracks metrics for monitoring and alerting.
/// </summary>
public sealed class DeadLetterQueueService : IDisposable
{
    private readonly KafkaProducerService _producer;
    private readonly KafkaSettings _settings;
    private readonly ILogger<DeadLetterQueueService> _logger;
    private readonly string _serviceName;
    
    // Metrics
    private long _messagesEnqueued;
    private long _enqueueFailed;
    private readonly Dictionary<string, long> _errorTypeCount = new();
    private readonly object _metricsLock = new();

    public DeadLetterQueueService(
        KafkaProducerService producer,
        IOptions<KafkaSettings> settings,
        ILogger<DeadLetterQueueService> logger,
        string serviceName)
    {
        _producer = producer;
        _settings = settings.Value;
        _logger = logger;
        _serviceName = serviceName;
    }

    /// <summary>
    /// Sends a failed message to the Dead Letter Queue.
    /// </summary>
    /// <param name="originalResult">The original Kafka consume result.</param>
    /// <param name="exception">The exception that caused the failure.</param>
    /// <param name="retryCount">Number of retries attempted.</param>
    /// <param name="metadata">Additional context about the failure.</param>
    public async Task<bool> SendToDeadLetterQueueAsync(
        ConsumeResult<string, byte[]> originalResult,
        Exception exception,
        int retryCount = 0,
        Dictionary<string, string>? metadata = null)
    {
        var dlqMessage = CreateDeadLetterMessage(originalResult, exception, retryCount, metadata);
        return await SendToDlqAsync(dlqMessage, originalResult.Message.Key);
    }

    /// <summary>
    /// Sends a failed message to the Dead Letter Queue with a specific reason.
    /// </summary>
    public async Task<bool> SendToDeadLetterQueueAsync(
        ConsumeResult<string, byte[]> originalResult,
        DlqReason reason,
        string errorMessage,
        int retryCount = 0,
        Dictionary<string, string>? metadata = null)
    {
        var dlqMessage = new DeadLetterMessage
        {
            OriginalTopic = originalResult.Topic,
            OriginalPartition = originalResult.Partition.Value,
            OriginalOffset = originalResult.Offset.Value,
            OriginalKey = originalResult.Message.Key,
            Payload = Convert.ToBase64String(originalResult.Message.Value),
            FailedService = _serviceName,
            ErrorType = reason.ToString(),
            ErrorMessage = errorMessage,
            RetryCount = retryCount,
            OriginalTimestamp = originalResult.Message.Timestamp.UtcDateTime,
            Metadata = metadata ?? []
        };
        
        return await SendToDlqAsync(dlqMessage, originalResult.Message.Key);
    }

    /// <summary>
    /// Creates a DLQ message for a validation error (no exception).
    /// </summary>
    public async Task<bool> SendValidationErrorAsync(
        ConsumeResult<string, byte[]> originalResult,
        string validationError,
        Dictionary<string, string>? metadata = null)
    {
        return await SendToDeadLetterQueueAsync(
            originalResult,
            DlqReason.ValidationError,
            validationError,
            metadata: metadata);
    }

    private DeadLetterMessage CreateDeadLetterMessage(
        ConsumeResult<string, byte[]> originalResult,
        Exception exception,
        int retryCount,
        Dictionary<string, string>? metadata)
    {
        var reason = ClassifyException(exception);
        
        return new DeadLetterMessage
        {
            OriginalTopic = originalResult.Topic,
            OriginalPartition = originalResult.Partition.Value,
            OriginalOffset = originalResult.Offset.Value,
            OriginalKey = originalResult.Message.Key,
            Payload = Convert.ToBase64String(originalResult.Message.Value),
            FailedService = _serviceName,
            ErrorType = reason.ToString(),
            ErrorMessage = exception.Message,
            StackTrace = exception.StackTrace,
            RetryCount = retryCount,
            OriginalTimestamp = originalResult.Message.Timestamp.UtcDateTime,
            Metadata = metadata ?? []
        };
    }

    private async Task<bool> SendToDlqAsync(DeadLetterMessage message, string? key)
    {
        try
        {
            var dlqBytes = message.ToBytes();
            var dlqKey = key ?? message.Id;
            
            await _producer.ProduceAsync(_settings.DlqTopic, dlqKey, dlqBytes);
            
            Interlocked.Increment(ref _messagesEnqueued);
            IncrementErrorTypeCount(message.ErrorType);
            
            _logger.LogWarning(
                "Message sent to DLQ. Topic: {Topic}, Partition: {Partition}, Offset: {Offset}, " +
                "ErrorType: {ErrorType}, Service: {Service}",
                message.OriginalTopic,
                message.OriginalPartition,
                message.OriginalOffset,
                message.ErrorType,
                message.FailedService);
            
            return true;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _enqueueFailed);
            
            _logger.LogError(ex,
                "Failed to send message to DLQ. Topic: {Topic}, Offset: {Offset}",
                message.OriginalTopic,
                message.OriginalOffset);
            
            return false;
        }
    }

    private static DlqReason ClassifyException(Exception exception)
    {
        // Classify based on exception type name to avoid tight coupling to specific packages
        var typeName = exception.GetType().FullName ?? exception.GetType().Name;
        
        return typeName switch
        {
            _ when typeName.Contains("InvalidProtocolBuffer") => DlqReason.DeserializationError,
            _ when typeName.Contains("JsonException") => DlqReason.DeserializationError,
            _ when typeName.Contains("FormatException") => DlqReason.DeserializationError,
            _ when exception is ArgumentException or ArgumentNullException => DlqReason.ValidationError,
            _ when exception is TimeoutException or OperationCanceledException => DlqReason.TimeoutError,
            _ when typeName.Contains("SqlException") => DlqReason.DownstreamError,
            _ when typeName.Contains("RedisException") => DlqReason.DownstreamError,
            _ when typeName.Contains("Redis") => DlqReason.DownstreamError,
            _ => DlqReason.ProcessingError
        };
    }

    private void IncrementErrorTypeCount(string errorType)
    {
        lock (_metricsLock)
        {
            if (!_errorTypeCount.TryGetValue(errorType, out var count))
            {
                count = 0;
            }
            _errorTypeCount[errorType] = count + 1;
        }
    }

    /// <summary>
    /// Total messages sent to DLQ.
    /// </summary>
    public long MessagesEnqueued => Interlocked.Read(ref _messagesEnqueued);
    
    /// <summary>
    /// Number of times sending to DLQ failed.
    /// </summary>
    public long EnqueueFailed => Interlocked.Read(ref _enqueueFailed);
    
    /// <summary>
    /// Gets error counts by type.
    /// </summary>
    public IReadOnlyDictionary<string, long> GetErrorTypeCounts()
    {
        lock (_metricsLock)
        {
            return new Dictionary<string, long>(_errorTypeCount);
        }
    }

    public void Dispose()
    {
        // Producer is disposed separately
    }
}

/// <summary>
/// Factory for creating DLQ services with service-specific configuration.
/// </summary>
public sealed class DeadLetterQueueServiceFactory
{
    private readonly KafkaProducerService _producer;
    private readonly IOptions<KafkaSettings> _settings;
    private readonly ILoggerFactory _loggerFactory;

    public DeadLetterQueueServiceFactory(
        KafkaProducerService producer,
        IOptions<KafkaSettings> settings,
        ILoggerFactory loggerFactory)
    {
        _producer = producer;
        _settings = settings;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Creates a DLQ service for a specific service name.
    /// </summary>
    public DeadLetterQueueService Create(string serviceName)
    {
        var logger = _loggerFactory.CreateLogger<DeadLetterQueueService>();
        return new DeadLetterQueueService(_producer, _settings, logger, serviceName);
    }
}
