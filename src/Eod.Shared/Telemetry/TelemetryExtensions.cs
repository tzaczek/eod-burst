using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Eod.Shared.Telemetry;

/// <summary>
/// OpenTelemetry configuration extensions for consistent observability across services.
/// </summary>
public static class TelemetryExtensions
{
    /// <summary>
    /// Adds OpenTelemetry tracing, metrics, and logging to the service.
    /// </summary>
    public static IServiceCollection AddEodTelemetry(
        this IServiceCollection services,
        string serviceName,
        string serviceVersion = "1.0.0")
    {
        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(
                serviceName: serviceName,
                serviceVersion: serviceVersion,
                serviceInstanceId: Environment.MachineName)
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment"] = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development",
                ["host.name"] = Environment.MachineName
            });

        // Get OTLP endpoint from environment
        var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") 
            ?? "http://localhost:4317";

        // Add Tracing
        services.AddOpenTelemetry()
            .WithTracing(builder =>
            {
                builder
                    .SetResourceBuilder(resourceBuilder)
                    // Auto-instrumentation for common libraries
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health");
                    })
                    .AddHttpClientInstrumentation(options =>
                    {
                        options.RecordException = true;
                    })
                    // Redis instrumentation
                    .AddRedisInstrumentation(options =>
                    {
                        options.SetVerboseDatabaseStatements = true;
                    })
                    // SQL Server instrumentation
                    .AddSqlClientInstrumentation(options =>
                    {
                        options.SetDbStatementForText = true;
                        options.RecordException = true;
                    })
                    // Custom sources for our services
                    .AddSource(EodActivitySource.SourceName)
                    .AddSource("Confluent.Kafka")
                    // Export to OTLP (Jaeger/Tempo)
                    .AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(otlpEndpoint);
                    })
                    // Also export to console for debugging
                    .AddConsoleExporter();
            })
            .WithMetrics(builder =>
            {
                builder
                    .SetResourceBuilder(resourceBuilder)
                    // Runtime metrics
                    .AddRuntimeInstrumentation()
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    // Custom metrics
                    .AddMeter(EodMetrics.MeterName)
                    // Export to OTLP (Prometheus)
                    .AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(otlpEndpoint);
                    })
                    .AddPrometheusExporter();
            });

        // Add logging integration
        services.AddLogging(logging =>
        {
            logging.AddOpenTelemetry(options =>
            {
                options.SetResourceBuilder(resourceBuilder);
                options.IncludeFormattedMessage = true;
                options.IncludeScopes = true;
                options.AddOtlpExporter(otlp =>
                {
                    otlp.Endpoint = new Uri(otlpEndpoint);
                });
            });
        });

        // Register telemetry services
        services.AddSingleton<EodActivitySource>();
        services.AddSingleton<EodMetrics>();

        return services;
    }
}

/// <summary>
/// ActivitySource for distributed tracing across EOD services.
/// </summary>
public sealed class EodActivitySource
{
    public const string SourceName = "Eod.Burst";
    
    private readonly ActivitySource _activitySource;

    public EodActivitySource()
    {
        _activitySource = new ActivitySource(SourceName, "1.0.0");
    }

    /// <summary>
    /// Starts a new activity for trade ingestion.
    /// </summary>
    public Activity? StartIngestion(string execId, string symbol)
    {
        var activity = _activitySource.StartActivity("ingestion.process_trade", ActivityKind.Consumer);
        activity?.SetTag("trade.exec_id", execId);
        activity?.SetTag("trade.symbol", symbol);
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.destination", "trades.raw");
        return activity;
    }

    /// <summary>
    /// Starts a new activity for P&L calculation.
    /// </summary>
    public Activity? StartPnlCalculation(string traderId, string symbol)
    {
        var activity = _activitySource.StartActivity("pnl.calculate", ActivityKind.Internal);
        activity?.SetTag("trader.id", traderId);
        activity?.SetTag("trade.symbol", symbol);
        return activity;
    }

    /// <summary>
    /// Starts a new activity for position update.
    /// </summary>
    public Activity? StartPositionUpdate(string traderId, string symbol, long quantity)
    {
        var activity = _activitySource.StartActivity("position.update", ActivityKind.Internal);
        activity?.SetTag("trader.id", traderId);
        activity?.SetTag("trade.symbol", symbol);
        activity?.SetTag("position.quantity", quantity);
        return activity;
    }

    /// <summary>
    /// Starts a new activity for Redis publish.
    /// </summary>
    public Activity? StartRedisPublish(string channel)
    {
        var activity = _activitySource.StartActivity("redis.publish", ActivityKind.Producer);
        activity?.SetTag("db.system", "redis");
        activity?.SetTag("db.operation", "PUBLISH");
        activity?.SetTag("messaging.destination", channel);
        return activity;
    }

    /// <summary>
    /// Starts a new activity for trade enrichment.
    /// </summary>
    public Activity? StartEnrichment(string execId)
    {
        var activity = _activitySource.StartActivity("regulatory.enrich", ActivityKind.Internal);
        activity?.SetTag("trade.exec_id", execId);
        return activity;
    }

    /// <summary>
    /// Starts a new activity for bulk SQL insert.
    /// </summary>
    public Activity? StartBulkInsert(int batchSize)
    {
        var activity = _activitySource.StartActivity("sql.bulk_insert", ActivityKind.Client);
        activity?.SetTag("db.system", "mssql");
        activity?.SetTag("db.operation", "BULK INSERT");
        activity?.SetTag("batch.size", batchSize);
        return activity;
    }

    /// <summary>
    /// Starts a new activity for Kafka produce.
    /// </summary>
    public Activity? StartKafkaProduce(string topic, string key)
    {
        var activity = _activitySource.StartActivity("kafka.produce", ActivityKind.Producer);
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.destination", topic);
        activity?.SetTag("messaging.kafka.message.key", key);
        return activity;
    }

    /// <summary>
    /// Starts a new activity for Kafka consume.
    /// </summary>
    public Activity? StartKafkaConsume(string topic, int partition, long offset)
    {
        var activity = _activitySource.StartActivity("kafka.consume", ActivityKind.Consumer);
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.source", topic);
        activity?.SetTag("messaging.kafka.partition", partition);
        activity?.SetTag("messaging.kafka.offset", offset);
        return activity;
    }

    /// <summary>
    /// Starts a new activity for S3 archive.
    /// </summary>
    public Activity? StartS3Archive(string bucket, int messageCount)
    {
        var activity = _activitySource.StartActivity("s3.archive", ActivityKind.Client);
        activity?.SetTag("cloud.provider", "aws");
        activity?.SetTag("aws.s3.bucket", bucket);
        activity?.SetTag("batch.size", messageCount);
        return activity;
    }
}

/// <summary>
/// Custom metrics for EOD system monitoring.
/// </summary>
public sealed class EodMetrics
{
    public const string MeterName = "Eod.Burst";
    
    private readonly Meter _meter;
    
    // Counters
    private readonly Counter<long> _tradesIngested;
    private readonly Counter<long> _tradesProcessed;
    private readonly Counter<long> _tradesInserted;
    private readonly Counter<long> _errors;
    private readonly Counter<long> _dlqMessages;
    private readonly Counter<long> _schemaRegistrations;
    private readonly Counter<long> _schemaValidations;
    private readonly Counter<long> _schemaCacheHits;
    
    // Histograms
    private readonly Histogram<double> _ingestionLatency;
    private readonly Histogram<double> _pnlCalculationLatency;
    private readonly Histogram<double> _bulkInsertLatency;
    private readonly Histogram<double> _enrichmentLatency;
    
    // Gauges (using ObservableGauge)
    private long _kafkaConsumerLag;
    private long _positionCount;
    private long _traderCount;
    private int _bufferDepth;

    public EodMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");
        
        // Counters
        _tradesIngested = _meter.CreateCounter<long>(
            "eod.trades.ingested",
            unit: "trades",
            description: "Total number of trades ingested from FIX gateway");
        
        _tradesProcessed = _meter.CreateCounter<long>(
            "eod.trades.processed",
            unit: "trades",
            description: "Total number of trades processed by P&L engine");
        
        _tradesInserted = _meter.CreateCounter<long>(
            "eod.trades.inserted",
            unit: "trades",
            description: "Total number of trades inserted to SQL Server");
        
        _errors = _meter.CreateCounter<long>(
            "eod.errors",
            unit: "errors",
            description: "Total number of errors by type");
        
        _dlqMessages = _meter.CreateCounter<long>(
            "eod.dlq.messages",
            unit: "messages",
            description: "Total number of messages sent to Dead Letter Queue");
        
        _schemaRegistrations = _meter.CreateCounter<long>(
            "eod.schema.registrations",
            unit: "registrations",
            description: "Total number of schemas registered with Schema Registry");
        
        _schemaValidations = _meter.CreateCounter<long>(
            "eod.schema.validations",
            unit: "validations",
            description: "Total number of schema validations performed");
        
        _schemaCacheHits = _meter.CreateCounter<long>(
            "eod.schema.cache_hits",
            unit: "hits",
            description: "Total number of schema cache hits");
        
        // Histograms
        _ingestionLatency = _meter.CreateHistogram<double>(
            "eod.ingestion.latency",
            unit: "ms",
            description: "Time to ingest and publish a trade to Kafka");
        
        _pnlCalculationLatency = _meter.CreateHistogram<double>(
            "eod.pnl.calculation.latency",
            unit: "ms",
            description: "Time to calculate P&L for a trade");
        
        _bulkInsertLatency = _meter.CreateHistogram<double>(
            "eod.bulk_insert.latency",
            unit: "ms",
            description: "Time to bulk insert a batch to SQL Server");
        
        _enrichmentLatency = _meter.CreateHistogram<double>(
            "eod.enrichment.latency",
            unit: "ms",
            description: "Time to enrich a trade with reference data");
        
        // Observable Gauges
        _meter.CreateObservableGauge(
            "eod.kafka.consumer.lag",
            () => _kafkaConsumerLag,
            unit: "messages",
            description: "Kafka consumer lag across all partitions");
        
        _meter.CreateObservableGauge(
            "eod.positions.count",
            () => _positionCount,
            unit: "positions",
            description: "Number of unique positions in memory");
        
        _meter.CreateObservableGauge(
            "eod.traders.count",
            () => _traderCount,
            unit: "traders",
            description: "Number of unique traders with positions");
        
        _meter.CreateObservableGauge(
            "eod.buffer.depth",
            () => _bufferDepth,
            unit: "messages",
            description: "Current depth of internal buffers");
    }

    // Counter methods
    public void IncrementTradesIngested(string symbol) =>
        _tradesIngested.Add(1, new KeyValuePair<string, object?>("symbol", symbol));
    
    public void IncrementTradesProcessed(string traderId, string symbol) =>
        _tradesProcessed.Add(1, 
            new KeyValuePair<string, object?>("trader_id", traderId),
            new KeyValuePair<string, object?>("symbol", symbol));
    
    public void IncrementTradesInserted(int count) =>
        _tradesInserted.Add(count);
    
    public void IncrementErrors(string errorType, string service) =>
        _errors.Add(1,
            new KeyValuePair<string, object?>("error_type", errorType),
            new KeyValuePair<string, object?>("service", service));
    
    public void IncrementDlqMessages(string service, string reason) =>
        _dlqMessages.Add(1,
            new KeyValuePair<string, object?>("service", service),
            new KeyValuePair<string, object?>("reason", reason));
    
    public void IncrementSchemaRegistrations(string subject) =>
        _schemaRegistrations.Add(1,
            new KeyValuePair<string, object?>("subject", subject));
    
    public void IncrementSchemaValidations(string subject, bool isValid) =>
        _schemaValidations.Add(1,
            new KeyValuePair<string, object?>("subject", subject),
            new KeyValuePair<string, object?>("is_valid", isValid));
    
    public void IncrementSchemaCacheHits(bool isHit) =>
        _schemaCacheHits.Add(1,
            new KeyValuePair<string, object?>("is_hit", isHit));

    // Histogram methods
    public void RecordIngestionLatency(double milliseconds, string symbol) =>
        _ingestionLatency.Record(milliseconds, new KeyValuePair<string, object?>("symbol", symbol));
    
    public void RecordPnlCalculationLatency(double milliseconds) =>
        _pnlCalculationLatency.Record(milliseconds);
    
    public void RecordBulkInsertLatency(double milliseconds, int batchSize) =>
        _bulkInsertLatency.Record(milliseconds, new KeyValuePair<string, object?>("batch_size", batchSize));
    
    public void RecordEnrichmentLatency(double milliseconds) =>
        _enrichmentLatency.Record(milliseconds);

    // Gauge setters
    public void SetConsumerLag(long lag) => _kafkaConsumerLag = lag;
    public void SetPositionCount(long count) => _positionCount = count;
    public void SetTraderCount(long count) => _traderCount = count;
    public void SetBufferDepth(int depth) => _bufferDepth = depth;
}
