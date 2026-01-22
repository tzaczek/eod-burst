namespace Eod.Shared.Configuration;

/// <summary>
/// MinIO/S3 configuration settings. Bind from appsettings.json section "Minio".
/// </summary>
public sealed class MinioSettings
{
    public const string SectionName = "Minio";
    
    /// <summary>
    /// MinIO endpoint (e.g., "localhost:9000" or "minio:9000")
    /// </summary>
    public required string Endpoint { get; init; }
    
    /// <summary>
    /// Access key (username)
    /// </summary>
    public required string AccessKey { get; init; }
    
    /// <summary>
    /// Secret key (password)
    /// </summary>
    public required string SecretKey { get; init; }
    
    /// <summary>
    /// Bucket name for raw message archive
    /// </summary>
    public string ArchiveBucket { get; init; } = "eod-archive";
    
    /// <summary>
    /// Use SSL/TLS
    /// </summary>
    public bool UseSsl { get; init; } = false;
    
    /// <summary>
    /// Buffer size before flushing to S3
    /// </summary>
    public int BufferSize { get; init; } = 10000;
    
    /// <summary>
    /// Flush interval in milliseconds
    /// </summary>
    public int FlushIntervalMs { get; init; } = 5000;
}
