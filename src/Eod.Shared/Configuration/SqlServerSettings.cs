namespace Eod.Shared.Configuration;

/// <summary>
/// SQL Server configuration settings. Bind from appsettings.json section "SqlServer".
/// </summary>
public sealed class SqlServerSettings
{
    public const string SectionName = "SqlServer";
    
    /// <summary>
    /// SQL Server connection string
    /// </summary>
    public required string ConnectionString { get; init; }
    
    /// <summary>
    /// Bulk insert batch size
    /// </summary>
    public int BulkBatchSize { get; init; } = 5000;
    
    /// <summary>
    /// Bulk copy timeout in seconds
    /// </summary>
    public int BulkCopyTimeoutSeconds { get; init; } = 60;
    
    /// <summary>
    /// Enable streaming for bulk operations
    /// </summary>
    public bool EnableStreaming { get; init; } = true;
    
    /// <summary>
    /// Max connection pool size
    /// </summary>
    public int MaxPoolSize { get; init; } = 100;
    
    /// <summary>
    /// Command timeout in seconds
    /// </summary>
    public int CommandTimeoutSeconds { get; init; } = 30;
}
