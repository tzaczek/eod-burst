using Eod.FlashPnl.Services;
using Eod.Shared.Configuration;
using Eod.Shared.Health;
using Eod.Shared.Kafka;
using Eod.Shared.Redis;
using Eod.Shared.Resilience;
using Eod.Shared.Schema;
using Eod.Shared.Telemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// Configure settings
builder.Services.Configure<KafkaSettings>(
    builder.Configuration.GetSection(KafkaSettings.SectionName));
builder.Services.Configure<RedisSettings>(
    builder.Configuration.GetSection(RedisSettings.SectionName));
builder.Services.Configure<SchemaRegistrySettings>(
    builder.Configuration.GetSection(SchemaRegistrySettings.SectionName));

// Register resilience services (Circuit Breaker pattern)
builder.Services.AddSingleton<ICircuitBreakerFactory, CircuitBreakerFactory>();

// Register infrastructure services
builder.Services.AddSingleton<KafkaConsumerService>();
builder.Services.AddSingleton<KafkaProducerService>();
builder.Services.AddSingleton<SchemaRegistryService>();
builder.Services.AddSingleton<SchemaValidatedConsumerService>();
builder.Services.AddSingleton<RedisService>();
builder.Services.AddSingleton<IRedisService>(sp => sp.GetRequiredService<RedisService>());

// Register Dead Letter Queue service
builder.Services.AddSingleton(sp => new DeadLetterQueueService(
    sp.GetRequiredService<KafkaProducerService>(),
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<KafkaSettings>>(),
    sp.GetRequiredService<ILoggerFactory>().CreateLogger<DeadLetterQueueService>(),
    "flash-pnl"));

// Register resilient wrapper for Redis (with circuit breaker protection)
builder.Services.AddSingleton<ResilientRedisService>();

// Register domain services
builder.Services.AddSingleton<PositionStore>();
builder.Services.AddSingleton<PriceService>();
builder.Services.AddSingleton<ServiceHealthCheck>();

// Register background services
builder.Services.AddHostedService<FlashPnlService>();
builder.Services.AddHostedService<PriceUpdateService>(); // Simulates price feeds

// Add health checks
builder.Services.AddHealthChecks()
    .AddCheck<ServiceHealthCheck>("service");

// Configure OpenTelemetry
builder.Services.AddEodTelemetry("eod-flash-pnl", "1.0.0");

var app = builder.Build();

// Map health endpoint
app.MapHealthChecks("/health");

// Metrics endpoint (includes circuit breaker status)
app.MapGet("/metrics", (
    KafkaConsumerService consumer, 
    PositionStore positions,
    ResilientRedisService redis,
    PriceService prices,
    DeadLetterQueueService dlq,
    SchemaRegistryService schemaRegistry) => new
{
    MessagesConsumed = consumer.MessagesConsumed,
    ConsumeErrors = consumer.ConsumeErrors,
    ConsumerLag = consumer.GetTotalLag(),
    UniquePositions = positions.GetPositionCount(),
    UniqueTraders = positions.GetTraderCount(),
    // Schema Registry metrics
    SchemaRegistry = schemaRegistry.GetMetrics(),
    // Dead Letter Queue metrics
    DeadLetterQueue = new
    {
        dlq.MessagesEnqueued,
        dlq.EnqueueFailed,
        ErrorsByType = dlq.GetErrorTypeCounts()
    },
    // Circuit breaker metrics
    CircuitBreaker = new
    {
        Publish = new
        {
            State = redis.PublishCircuitState.ToString(),
            redis.PublishMetrics.TotalRequests,
            redis.PublishMetrics.SuccessfulRequests,
            redis.PublishMetrics.FailedRequests,
            redis.PublishMetrics.RejectedRequests,
            SuccessRate = $"{redis.PublishMetrics.SuccessRate:F1}%"
        },
        Query = new
        {
            State = redis.QueryCircuitState.ToString(),
            redis.QueryMetrics.TotalRequests,
            redis.QueryMetrics.SuccessfulRequests,
            redis.QueryMetrics.FailedRequests,
            redis.QueryMetrics.RejectedRequests,
            SuccessRate = $"{redis.QueryMetrics.SuccessRate:F1}%"
        }
    },
    // Price cache metrics
    PriceCache = new
    {
        prices.CacheHits,
        prices.CacheMisses,
        prices.RedisFallbacks,
        HitRate = prices.CacheHits + prices.CacheMisses > 0
            ? $"{100.0 * prices.CacheHits / (prices.CacheHits + prices.CacheMisses):F1}%"
            : "N/A"
    }
});

// Prometheus metrics endpoint
app.MapPrometheusScrapingEndpoint("/metrics/prometheus");

// Position query endpoint (for debugging)
app.MapGet("/position/{traderId}/{symbol}", 
    async (string traderId, string symbol, ResilientRedisService redis) =>
{
    var qty = await redis.GetPositionAsync(traderId, symbol);
    return qty.HasValue 
        ? Results.Ok(new { TraderId = traderId, Symbol = symbol, Quantity = qty.Value })
        : Results.NotFound();
});

// Circuit breaker status endpoint
app.MapGet("/circuit-breaker", (ResilientRedisService redis, PriceService prices) => new
{
    RedisPublish = new
    {
        State = redis.PublishCircuitState.ToString(),
        Metrics = redis.PublishMetrics
    },
    RedisQuery = new
    {
        State = redis.QueryCircuitState.ToString(),
        Metrics = redis.QueryMetrics
    },
    PriceCacheCircuit = prices.RedisCircuitState.ToString()
});

await app.RunAsync();
