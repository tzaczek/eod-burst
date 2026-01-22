using Eod.Regulatory.Services;
using Eod.Shared.Configuration;
using Eod.Shared.Data;
using Eod.Shared.Health;
using Eod.Shared.Kafka;
using Eod.Shared.Schema;
using Eod.Shared.Telemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// Configure settings
builder.Services.Configure<KafkaSettings>(
    builder.Configuration.GetSection(KafkaSettings.SectionName));
builder.Services.Configure<SqlServerSettings>(
    builder.Configuration.GetSection(SqlServerSettings.SectionName));
builder.Services.Configure<SchemaRegistrySettings>(
    builder.Configuration.GetSection(SchemaRegistrySettings.SectionName));

// Configure Entity Framework Core with SQL Server
var sqlSettings = builder.Configuration.GetSection(SqlServerSettings.SectionName).Get<SqlServerSettings>();
builder.Services.AddDbContext<EodDbContext>(options =>
{
    options.UseSqlServer(sqlSettings!.ConnectionString, sqlOptions =>
    {
        sqlOptions.CommandTimeout(sqlSettings.CommandTimeoutSeconds);
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: null);
    });
});

// Register database migration service (runs before other hosted services)
builder.Services.AddHostedService<DatabaseMigrationService>();

// Register services
builder.Services.AddSingleton<KafkaConsumerService>();
builder.Services.AddSingleton<KafkaProducerService>();
builder.Services.AddSingleton<SchemaRegistryService>();
builder.Services.AddSingleton<SchemaValidatedConsumerService>();
builder.Services.AddSingleton<ReferenceDataService>();
builder.Services.AddSingleton<BulkInsertService>();
builder.Services.AddSingleton<ServiceHealthCheck>();

// Register Dead Letter Queue service
builder.Services.AddSingleton(sp => new DeadLetterQueueService(
    sp.GetRequiredService<KafkaProducerService>(),
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<KafkaSettings>>(),
    sp.GetRequiredService<ILoggerFactory>().CreateLogger<DeadLetterQueueService>(),
    "regulatory"));

// Register background services
builder.Services.AddHostedService<RegulatoryService>();

// Add health checks
builder.Services.AddHealthChecks()
    .AddCheck<ServiceHealthCheck>("service");

// Configure OpenTelemetry
builder.Services.AddEodTelemetry("eod-regulatory", "1.0.0");

var app = builder.Build();

// Map health endpoint
app.MapHealthChecks("/health");

// Metrics endpoint
app.MapGet("/metrics", (
    KafkaConsumerService consumer, 
    BulkInsertService bulkInsert,
    DeadLetterQueueService dlq,
    SchemaRegistryService schemaRegistry) => new
{
    MessagesConsumed = consumer.MessagesConsumed,
    ConsumeErrors = consumer.ConsumeErrors,
    ConsumerLag = consumer.GetTotalLag(),
    TradesInserted = bulkInsert.TotalInserted,
    InsertErrors = bulkInsert.TotalErrors,
    BatchesProcessed = bulkInsert.BatchesProcessed,
    // Schema Registry metrics
    SchemaRegistry = schemaRegistry.GetMetrics(),
    // Dead Letter Queue metrics
    DeadLetterQueue = new
    {
        dlq.MessagesEnqueued,
        dlq.EnqueueFailed,
        ErrorsByType = dlq.GetErrorTypeCounts()
    }
});

// Prometheus metrics endpoint
app.MapPrometheusScrapingEndpoint("/metrics/prometheus");

await app.RunAsync();
