using Eod.Ingestion.Services;
using Eod.Shared.Configuration;
using Eod.Shared.Health;
using Eod.Shared.Kafka;
using Eod.Shared.Resilience;
using Eod.Shared.Schema;
using Eod.Shared.Telemetry;

var builder = WebApplication.CreateBuilder(args);

// Configure settings
builder.Services.Configure<KafkaSettings>(
    builder.Configuration.GetSection(KafkaSettings.SectionName));
builder.Services.Configure<MinioSettings>(
    builder.Configuration.GetSection(MinioSettings.SectionName));
builder.Services.Configure<SchemaRegistrySettings>(
    builder.Configuration.GetSection(SchemaRegistrySettings.SectionName));

// Register resilience services (Circuit Breaker pattern)
builder.Services.AddSingleton<ICircuitBreakerFactory, CircuitBreakerFactory>();

// Register services
builder.Services.AddSingleton<KafkaProducerService>();
builder.Services.AddSingleton<SchemaRegistryService>();
builder.Services.AddSingleton<SchemaValidatedProducerService>();
builder.Services.AddSingleton<S3ArchiveService>();
builder.Services.AddSingleton<ServiceHealthCheck>();
builder.Services.AddSingleton<MessageChannel>();

// Register background services
builder.Services.AddHostedService<IngestionService>();
builder.Services.AddHostedService<FixSimulatorService>(); // For testing

// Add health checks
builder.Services.AddHealthChecks()
    .AddCheck<ServiceHealthCheck>("service");

// Configure OpenTelemetry
builder.Services.AddEodTelemetry("eod-ingestion", "1.0.0");

var app = builder.Build();

// Map health endpoint
app.MapHealthChecks("/health");
app.MapGet("/metrics", (KafkaProducerService producer, SchemaRegistryService schemaRegistry) => new
{
    MessagesSent = producer.MessagesSent,
    DeliveryErrors = producer.DeliveryErrors,
    SchemaRegistry = schemaRegistry.GetMetrics()
});

// Prometheus metrics endpoint
app.MapPrometheusScrapingEndpoint("/metrics/prometheus");

await app.RunAsync();
