using System.Text;
using System.Threading.Channels;
using Eod.Shared.Configuration;
using Eod.Shared.Resilience;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;

namespace Eod.Ingestion.Services;

/// <summary>
/// Asynchronously archives raw FIX messages to MinIO/S3.
/// Uses a bounded channel to decouple archiving from the hot path.
/// Implements Circuit Breaker pattern to prevent cascading failures during S3 outages.
/// </summary>
public sealed class S3ArchiveService : BackgroundService
{
    private readonly IMinioClient _minioClient;
    private readonly MinioSettings _settings;
    private readonly ICircuitBreaker _circuitBreaker;
    private readonly ILogger<S3ArchiveService> _logger;
    
    private readonly Channel<ArchiveItem> _archiveChannel;
    private readonly List<ArchiveItem> _buffer;
    private DateTime _lastFlushTime = DateTime.UtcNow;
    
    private long _archivedCount;
    private long _archiveErrors;
    private long _circuitBreakerRejections;

    public S3ArchiveService(
        IOptions<MinioSettings> settings,
        ICircuitBreakerFactory circuitBreakerFactory,
        ILogger<S3ArchiveService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        
        // Configure circuit breaker for storage operations
        _circuitBreaker = circuitBreakerFactory.GetOrCreate(
            "S3Archive",
            CircuitBreakerOptions.Storage with 
            { 
                Name = "S3Archive",
                FailureThreshold = 5,
                OpenDuration = TimeSpan.FromSeconds(30),
                ExceptionTypes = [typeof(Minio.Exceptions.MinioException), typeof(HttpRequestException)]
            });
        
        _minioClient = new MinioClient()
            .WithEndpoint(_settings.Endpoint)
            .WithCredentials(_settings.AccessKey, _settings.SecretKey)
            .WithSSL(_settings.UseSsl)
            .Build();
        
        _archiveChannel = Channel.CreateBounded<ArchiveItem>(
            new BoundedChannelOptions(_settings.BufferSize * 2)
            {
                FullMode = BoundedChannelFullMode.DropOldest, // Drop oldest under pressure
                SingleReader = true,
                SingleWriter = false
            });
        
        _buffer = new List<ArchiveItem>(_settings.BufferSize);
    }

    /// <summary>
    /// Queues a message for archiving. Non-blocking fire-and-forget.
    /// </summary>
    public ValueTask ArchiveAsync(byte[] rawBytes, long receiveTimestamp, CancellationToken ct = default)
    {
        var item = new ArchiveItem
        {
            RawBytes = rawBytes,
            ReceiveTimestamp = receiveTimestamp,
            EnqueuedAt = DateTime.UtcNow
        };
        
        // Try to write without waiting (fire-and-forget)
        if (_archiveChannel.Writer.TryWrite(item))
        {
            return ValueTask.CompletedTask;
        }
        
        // Channel full - log but don't block
        _logger.LogWarning("Archive channel full, message may be dropped");
        return ValueTask.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("S3 Archive service starting...");
        
        try
        {
            // Ensure bucket exists
            await EnsureBucketExistsAsync(stoppingToken);
            
            await foreach (var item in _archiveChannel.Reader.ReadAllAsync(stoppingToken))
            {
                _buffer.Add(item);
                
                // Flush when buffer is full or interval elapsed
                var shouldFlush = _buffer.Count >= _settings.BufferSize ||
                    (DateTime.UtcNow - _lastFlushTime).TotalMilliseconds >= _settings.FlushIntervalMs;
                
                if (shouldFlush && _buffer.Count > 0)
                {
                    await FlushBufferAsync(stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Final flush on shutdown
            if (_buffer.Count > 0)
            {
                await FlushBufferAsync(CancellationToken.None);
            }
        }
        
        _logger.LogInformation("S3 Archive service stopped. Total archived: {Count}", _archivedCount);
    }

    private async Task FlushBufferAsync(CancellationToken ct)
    {
        if (_buffer.Count == 0)
            return;

        // Check circuit breaker state before attempting to flush
        if (_circuitBreaker.State == CircuitBreakerState.Open)
        {
            Interlocked.Add(ref _circuitBreakerRejections, _buffer.Count);
            _logger.LogWarning(
                "Circuit breaker open - dropping {Count} messages from archive buffer", 
                _buffer.Count);
            _buffer.Clear();
            _lastFlushTime = DateTime.UtcNow;
            return;
        }

        var itemsToFlush = _buffer.ToList();
        _buffer.Clear();
        _lastFlushTime = DateTime.UtcNow;

        try
        {
            await _circuitBreaker.ExecuteAsync(async token =>
            {
                var timestamp = DateTime.UtcNow;
                var objectName = $"{timestamp:yyyy-MM-dd}/{timestamp:HH}/{timestamp:mm-ss-fff}_{Environment.MachineName}_{itemsToFlush.Count}.bin";
                
                // Concatenate all messages with length prefix
                using var stream = new MemoryStream();
                using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
                
                foreach (var item in itemsToFlush)
                {
                    writer.Write(item.ReceiveTimestamp);
                    writer.Write(item.RawBytes.Length);
                    writer.Write(item.RawBytes);
                }
                
                stream.Position = 0;
                
                // Upload to MinIO through circuit breaker
                await _minioClient.PutObjectAsync(new PutObjectArgs()
                    .WithBucket(_settings.ArchiveBucket)
                    .WithObject(objectName)
                    .WithStreamData(stream)
                    .WithObjectSize(stream.Length)
                    .WithContentType("application/octet-stream"), token);
                
                Interlocked.Add(ref _archivedCount, itemsToFlush.Count);
                
                _logger.LogDebug(
                    "Archived {Count} messages to {Bucket}/{Object}",
                    itemsToFlush.Count,
                    _settings.ArchiveBucket,
                    objectName);
            }, ct);
        }
        catch (CircuitBreakerOpenException)
        {
            Interlocked.Add(ref _circuitBreakerRejections, itemsToFlush.Count);
            _logger.LogWarning(
                "Circuit breaker tripped during flush - {Count} messages not archived", 
                itemsToFlush.Count);
        }
        catch (Exception ex)
        {
            Interlocked.Add(ref _archiveErrors, itemsToFlush.Count);
            _logger.LogError(ex, "Failed to archive {Count} messages", itemsToFlush.Count);
        }
    }

    private async Task EnsureBucketExistsAsync(CancellationToken ct)
    {
        var retryCount = 0;
        while (retryCount < 30)
        {
            try
            {
                var exists = await _minioClient.BucketExistsAsync(
                    new BucketExistsArgs().WithBucket(_settings.ArchiveBucket), ct);
                
                if (!exists)
                {
                    await _minioClient.MakeBucketAsync(
                        new MakeBucketArgs().WithBucket(_settings.ArchiveBucket), ct);
                    _logger.LogInformation("Created bucket: {Bucket}", _settings.ArchiveBucket);
                }
                
                return;
            }
            catch (Exception ex)
            {
                retryCount++;
                _logger.LogWarning(ex, 
                    "Waiting for MinIO... attempt {Count}/30", retryCount);
                await Task.Delay(2000, ct);
            }
        }
        
        throw new InvalidOperationException("Could not connect to MinIO");
    }

    public long ArchivedCount => Interlocked.Read(ref _archivedCount);
    public long ArchiveErrors => Interlocked.Read(ref _archiveErrors);
    public long CircuitBreakerRejections => Interlocked.Read(ref _circuitBreakerRejections);
    public CircuitBreakerState CircuitBreakerState => _circuitBreaker.State;
    public CircuitBreakerMetrics CircuitBreakerMetrics => _circuitBreaker.Metrics;

    private readonly struct ArchiveItem
    {
        public byte[] RawBytes { get; init; }
        public long ReceiveTimestamp { get; init; }
        public DateTime EnqueuedAt { get; init; }
    }
}
