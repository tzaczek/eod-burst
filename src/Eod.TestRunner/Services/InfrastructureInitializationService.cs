using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Eod.Shared.Configuration;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;

namespace Eod.TestRunner.Services;

/// <summary>
/// Service responsible for initializing infrastructure (Kafka topics, MinIO buckets) before tests.
/// This runs on startup to ensure all required resources exist.
/// </summary>
public sealed class InfrastructureInitializationService : BackgroundService
{
    private readonly ILogger<InfrastructureInitializationService> _logger;
    private readonly KafkaSettings _kafkaSettings;
    private readonly IConfiguration _configuration;
    private static bool _initialized = false;
    private static readonly SemaphoreSlim _initLock = new(1, 1);

    public InfrastructureInitializationService(
        IOptions<KafkaSettings> kafkaSettings,
        IConfiguration configuration,
        ILogger<InfrastructureInitializationService> logger)
    {
        _kafkaSettings = kafkaSettings.Value;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await InitializeInfrastructureAsync(stoppingToken);
    }

    /// <summary>
    /// Ensures all infrastructure is initialized. Safe to call multiple times.
    /// </summary>
    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            await InitializeInfrastructureAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task InitializeInfrastructureAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting infrastructure initialization...");

        var kafkaTask = InitializeKafkaTopicsAsync(cancellationToken);
        var minioTask = InitializeMinioBucketsAsync(cancellationToken);

        await Task.WhenAll(kafkaTask, minioTask);

        _initialized = true;
        _logger.LogInformation("Infrastructure initialization completed successfully");
    }

    private async Task InitializeKafkaTopicsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing Kafka topics...");

        var config = new AdminClientConfig
        {
            BootstrapServers = _kafkaSettings.BootstrapServers
        };

        using var adminClient = new AdminClientBuilder(config).Build();

        var topics = new[]
        {
            new TopicSpecification
            {
                Name = _kafkaSettings.TradesTopic,
                NumPartitions = 12,
                ReplicationFactor = 1,
                Configs = new Dictionary<string, string>
                {
                    ["retention.ms"] = "604800000", // 7 days
                    ["cleanup.policy"] = "delete"
                }
            },
            new TopicSpecification
            {
                Name = _kafkaSettings.DlqTopic,
                NumPartitions = 12,
                ReplicationFactor = 1,
                Configs = new Dictionary<string, string>
                {
                    ["retention.ms"] = "2592000000", // 30 days
                    ["cleanup.policy"] = "delete"
                }
            },
            new TopicSpecification
            {
                Name = _kafkaSettings.PricesTopic,
                NumPartitions = 6,
                ReplicationFactor = 1
            }
        };

        foreach (var topic in topics)
        {
            try
            {
                await adminClient.CreateTopicsAsync(new[] { topic });
                _logger.LogInformation("Created Kafka topic: {Topic}", topic.Name);
            }
            catch (CreateTopicsException ex) when (ex.Results.Any(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
            {
                _logger.LogDebug("Kafka topic already exists: {Topic}", topic.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create Kafka topic {Topic}, it may already exist", topic.Name);
            }
        }

        // List topics to verify
        var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(10));
        _logger.LogInformation("Kafka topics available: {Topics}",
            string.Join(", ", metadata.Topics.Select(t => t.Topic)));
    }

    private async Task InitializeMinioBucketsAsync(CancellationToken cancellationToken)
    {
        var minioEndpoint = _configuration["Minio:Endpoint"] ?? "minio:9000";
        var minioAccessKey = _configuration["Minio:AccessKey"] 
            ?? Environment.GetEnvironmentVariable("MINIO_ROOT_USER") 
            ?? throw new InvalidOperationException("Minio:AccessKey or MINIO_ROOT_USER environment variable is required");
        var minioSecretKey = _configuration["Minio:SecretKey"] 
            ?? Environment.GetEnvironmentVariable("MINIO_ROOT_PASSWORD") 
            ?? throw new InvalidOperationException("Minio:SecretKey or MINIO_ROOT_PASSWORD environment variable is required");

        _logger.LogInformation("Initializing MinIO buckets at {Endpoint}...", minioEndpoint);

        try
        {
            var minioClient = new MinioClient()
                .WithEndpoint(minioEndpoint)
                .WithCredentials(minioAccessKey, minioSecretKey)
                .WithSSL(false)
                .Build();

            var buckets = new[] { "trade-archives", "raw-messages", "backups" };

            foreach (var bucket in buckets)
            {
                try
                {
                    var exists = await minioClient.BucketExistsAsync(
                        new BucketExistsArgs().WithBucket(bucket), cancellationToken);

                    if (!exists)
                    {
                        await minioClient.MakeBucketAsync(
                            new MakeBucketArgs().WithBucket(bucket), cancellationToken);
                        _logger.LogInformation("Created MinIO bucket: {Bucket}", bucket);
                    }
                    else
                    {
                        _logger.LogDebug("MinIO bucket already exists: {Bucket}", bucket);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create MinIO bucket {Bucket}", bucket);
                }
            }

            _logger.LogInformation("MinIO buckets initialized");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize MinIO buckets - MinIO may not be available");
        }
    }
}
