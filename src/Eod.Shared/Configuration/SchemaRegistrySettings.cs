namespace Eod.Shared.Configuration;

/// <summary>
/// Schema Registry configuration settings. Bind from appsettings.json section "SchemaRegistry".
/// </summary>
public sealed class SchemaRegistrySettings
{
    public const string SectionName = "SchemaRegistry";
    
    /// <summary>
    /// Schema Registry URL (e.g., "http://localhost:8081" or "http://schema-registry:8081")
    /// </summary>
    public string Url { get; init; } = "http://localhost:8085";
    
    /// <summary>
    /// Enable schema validation for producers and consumers
    /// </summary>
    public bool Enabled { get; init; } = true;
    
    /// <summary>
    /// Auto-register schemas when producing messages
    /// </summary>
    public bool AutoRegisterSchemas { get; init; } = true;
    
    /// <summary>
    /// Schema compatibility level: BACKWARD, BACKWARD_TRANSITIVE, FORWARD, FORWARD_TRANSITIVE, FULL, FULL_TRANSITIVE, NONE
    /// </summary>
    public string CompatibilityLevel { get; init; } = "BACKWARD";
    
    /// <summary>
    /// Subject naming strategy: TopicName, RecordName, TopicRecordName
    /// </summary>
    public SubjectNamingStrategy SubjectNamingStrategy { get; init; } = SubjectNamingStrategy.TopicName;
    
    /// <summary>
    /// Timeout for schema registry HTTP requests in milliseconds
    /// </summary>
    public int RequestTimeoutMs { get; init; } = 30000;
    
    /// <summary>
    /// Maximum number of schemas to cache locally
    /// </summary>
    public int MaxCachedSchemas { get; init; } = 1000;
    
    /// <summary>
    /// Optional basic auth username
    /// </summary>
    public string? Username { get; init; }
    
    /// <summary>
    /// Optional basic auth password
    /// </summary>
    public string? Password { get; init; }
}

public enum SubjectNamingStrategy
{
    /// <summary>
    /// Uses topic name as subject (e.g., "trades.raw-value")
    /// </summary>
    TopicName,
    
    /// <summary>
    /// Uses record/message type name as subject (e.g., "TradeEnvelope")
    /// </summary>
    RecordName,
    
    /// <summary>
    /// Combines topic and record name (e.g., "trades.raw-TradeEnvelope")
    /// </summary>
    TopicRecordName
}
