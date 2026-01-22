using System.Text.Json;

namespace Eod.Shared.Kafka;

/// <summary>
/// Represents a message that failed processing and was sent to the Dead Letter Queue.
/// Contains the original message payload along with error details for debugging and reprocessing.
/// </summary>
public sealed class DeadLetterMessage
{
    /// <summary>
    /// Unique identifier for this DLQ entry.
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    
    /// <summary>
    /// The original topic the message was consumed from.
    /// </summary>
    public required string OriginalTopic { get; init; }
    
    /// <summary>
    /// The original partition the message was consumed from.
    /// </summary>
    public int OriginalPartition { get; init; }
    
    /// <summary>
    /// The original offset of the message.
    /// </summary>
    public long OriginalOffset { get; init; }
    
    /// <summary>
    /// The original message key.
    /// </summary>
    public string? OriginalKey { get; init; }
    
    /// <summary>
    /// The original message payload (base64 encoded if binary).
    /// </summary>
    public required string Payload { get; init; }
    
    /// <summary>
    /// The service that failed to process this message.
    /// </summary>
    public required string FailedService { get; init; }
    
    /// <summary>
    /// The error type/exception class name.
    /// </summary>
    public required string ErrorType { get; init; }
    
    /// <summary>
    /// The error message describing what went wrong.
    /// </summary>
    public required string ErrorMessage { get; init; }
    
    /// <summary>
    /// The full stack trace for debugging.
    /// </summary>
    public string? StackTrace { get; init; }
    
    /// <summary>
    /// Number of times this message has been retried before being sent to DLQ.
    /// </summary>
    public int RetryCount { get; init; }
    
    /// <summary>
    /// When the original message was received.
    /// </summary>
    public DateTime OriginalTimestamp { get; init; }
    
    /// <summary>
    /// When this message was sent to the DLQ.
    /// </summary>
    public DateTime DlqTimestamp { get; init; } = DateTime.UtcNow;
    
    /// <summary>
    /// Additional metadata/context about the failure.
    /// </summary>
    public Dictionary<string, string> Metadata { get; init; } = [];
    
    /// <summary>
    /// Serializes this DLQ message to JSON bytes for Kafka.
    /// </summary>
    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this);
    
    /// <summary>
    /// Deserializes a DLQ message from JSON bytes.
    /// </summary>
    public static DeadLetterMessage FromBytes(byte[] bytes) => 
        JsonSerializer.Deserialize<DeadLetterMessage>(bytes) 
        ?? throw new InvalidOperationException("Failed to deserialize DLQ message");
}

/// <summary>
/// Reason codes for why a message was sent to DLQ.
/// </summary>
public enum DlqReason
{
    /// <summary>
    /// Message deserialization failed (corrupted/invalid format).
    /// </summary>
    DeserializationError,
    
    /// <summary>
    /// Required fields are missing or invalid.
    /// </summary>
    ValidationError,
    
    /// <summary>
    /// Processing logic threw an exception.
    /// </summary>
    ProcessingError,
    
    /// <summary>
    /// Downstream service (Redis/SQL) failed after retries.
    /// </summary>
    DownstreamError,
    
    /// <summary>
    /// Message processing exceeded timeout.
    /// </summary>
    TimeoutError,
    
    /// <summary>
    /// Unknown/unhandled error.
    /// </summary>
    Unknown
}
