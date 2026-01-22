// Architecture data from ARCHITECTURE.md

export interface TechStackItem {
  layer: string;
  technology: string;
  icon: string;
  description: string;
  color: string;
  details: string;
  codeExample: {
    title: string;
    language: string;
    code: string;
    file?: string;
  };
}

export interface DesignPattern {
  name: string;
  implementation: string;
  purpose: string;
  icon: string;
}

export interface ServiceInfo {
  name: string;
  port: string;
  purpose: string;
  icon: string;
  category: 'application' | 'infrastructure' | 'observability';
}

export interface Capability {
  name: string;
  value: string;
  icon: string;
}

export const CAPABILITIES: Capability[] = [
  { name: 'Ingestion Throughput', value: '10,000+ trades/sec', icon: '🚀' },
  { name: 'P&L Latency', value: '< 100ms E2E', icon: '⚡' },
  { name: 'Burst Handling', value: '10x normal volume', icon: '📈' },
  { name: 'Regulatory Accuracy', value: '100% audit-compliant', icon: '🔒' },
  { name: 'Observability', value: 'Full distributed tracing', icon: '🔍' },
];

export const TECH_STACK: TechStackItem[] = [
  { 
    layer: 'Runtime', 
    technology: '.NET 8 / C# 12', 
    icon: '⚙️', 
    description: 'High-performance server runtime', 
    color: '#512BD4',
    details: 'The system runs on .NET 8 with C# 12, providing high-performance async processing with BackgroundService pattern for all long-running services. Features include native AOT compilation support, improved garbage collection, and excellent tooling for building distributed systems.',
    codeExample: {
      title: 'BackgroundService Pattern',
      language: 'csharp',
      file: 'FlashPnlService.cs',
      code: `public class FlashPnlService : BackgroundService
{
    private readonly IKafkaConsumerService _consumer;
    private readonly ICircuitBreakerFactory _circuitBreakerFactory;
    
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        await foreach (var message in _consumer
            .ConsumeAsync<TradeEnvelope>("trades.raw", stoppingToken))
        {
            await ProcessTradeAsync(message.Value);
        }
    }
}`
    }
  },
  { 
    layer: 'Messaging', 
    technology: 'Apache Kafka (KRaft)', 
    icon: '📨', 
    description: 'Distributed event streaming', 
    color: '#FF6B35',
    details: 'Kafka provides durable, partitioned event log for trade messages. Key features: Messages persisted to disk (survives broker restart), replay capability from any offset for debugging/recovery, partitioning for parallel processing with ordering per partition, consumer groups for independent reading, and 1M+ messages/sec throughput per broker. Uses KRaft mode (no Zookeeper) for simpler architecture and faster startup.',
    codeExample: {
      title: 'Kafka Producer Service',
      language: 'csharp',
      file: 'KafkaProducerService.cs',
      code: `public async Task ProduceAsync<T>(
    string topic, 
    string key, 
    T message) where T : IMessage<T>
{
    var deliveryResult = await _producer.ProduceAsync(
        topic,
        new Message<string, byte[]>
        {
            Key = key,
            Value = message.ToByteArray(),
            Headers = new Headers
            {
                { "trace-id", Encoding.UTF8.GetBytes(
                    Activity.Current?.TraceId.ToString() ?? "") }
            }
        });
    
    EodMetrics.IncrementMessagesProduced(topic);
}`
    }
  },
  { 
    layer: 'Cache', 
    technology: 'Redis 7', 
    icon: '💾', 
    description: 'In-memory data store', 
    color: '#DC382D',
    details: 'Redis serves as real-time position state and P&L pub/sub layer. Perfect data structures with HSET for positions (HSET positions:T123 AAPL 500), built-in pub/sub for pushing updates to subscribers, atomic operations with HINCRBY for thread-safe updates, and sub-millisecond latency (~0.1ms for hash operations). Configured with AOF persistence and LRU eviction policy.',
    codeExample: {
      title: 'Resilient Redis Service with Circuit Breaker',
      language: 'csharp',
      file: 'ResilientRedisService.cs',
      code: `public async Task<bool> PublishPositionAsync(
    string traderId, 
    string symbol, 
    Position position)
{
    return await _circuitBreaker.ExecuteAsync(async () =>
    {
        var key = $"positions:{traderId}";
        var field = symbol;
        var value = JsonSerializer.Serialize(position);
        
        await _database.HashSetAsync(key, field, value);
        await _subscriber.PublishAsync(
            RedisChannel.Literal($"pnl:{traderId}"),
            value);
        
        return true;
    });
}`
    }
  },
  { 
    layer: 'Database', 
    technology: 'SQL Server 2022', 
    icon: '🗄️', 
    description: 'ACID-compliant persistence', 
    color: '#CC2927',
    details: 'SQL Server provides regulatory trade persistence with ACID compliance. Key features: SqlBulkCopy for native .NET bulk insert (10x faster than EF), Entity Framework Core for code-first migrations and LINQ queries, temporal tables for built-in audit history (regulatory requirement), and table partitioning by date for efficient queries. Command timeout set to 60 seconds for bulk operations.',
    codeExample: {
      title: 'Entity Framework DbContext',
      language: 'csharp',
      file: 'EodDbContext.cs',
      code: `public class EodDbContext : DbContext
{
    public DbSet<Trade> Trades => Set<Trade>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Trade>(entity =>
        {
            entity.HasKey(e => e.TradeId);
            entity.HasIndex(e => e.ExecId).IsUnique();
            entity.HasIndex(e => e.ExecTimestampUtc);
            
            entity.Property(e => e.Price)
                .HasPrecision(18, 8);
        });
    }
}`
    }
  },
  { 
    layer: 'Object Storage', 
    technology: 'MinIO', 
    icon: '📦', 
    description: 'S3-compatible archive', 
    color: '#C72C48',
    details: 'MinIO provides S3-compatible object storage for raw FIX message archive. Solves the problem: Exchange updates FIX spec → Parser crashes → Trades lost. Solution: Archive raw bytes BEFORE parsing. Recovery: Download from S3, replay through fixed parser. Same code works with AWS S3 for production migration.',
    codeExample: {
      title: 'S3 Archive Service',
      language: 'csharp',
      file: 'S3ArchiveService.cs',
      code: `public async Task ArchiveAsync(
    byte[] rawFixMessage, 
    DateTime timestamp)
{
    await _circuitBreaker.ExecuteAsync(async () =>
    {
        var key = $"fix/{timestamp:yyyy/MM/dd/HH}/" +
                  $"{Guid.NewGuid()}.fix";
        
        using var stream = new MemoryStream(rawFixMessage);
        
        await _minioClient.PutObjectAsync(
            new PutObjectArgs()
                .WithBucket(_archiveBucket)
                .WithObject(key)
                .WithStreamData(stream)
                .WithObjectSize(rawFixMessage.Length)
                .WithContentType("application/octet-stream"));
    });
}`
    }
  },
  { 
    layer: 'Serialization', 
    technology: 'Protocol Buffers', 
    icon: '📄', 
    description: 'Binary message encoding', 
    color: '#34A853',
    details: 'Protocol Buffers (Protobuf) provides efficient binary serialization for all message types. TradeEnvelope contains raw FIX bytes for replay, timestamps, exec_id, symbol, quantity, price (as mantissa to avoid floating point), side enum, and trader_id. Price stored as mantissa (Price * 10^8) to eliminate floating point precision issues in financial calculations.',
    codeExample: {
      title: 'Trade Envelope Proto Definition',
      language: 'protobuf',
      file: 'trade_envelope.proto',
      code: `message TradeEnvelope {
    bytes raw_fix = 1;              // Original FIX for replay
    int64 receive_timestamp_ticks = 2;
    string gateway_id = 4;
    
    string exec_id = 10;            // Unique from exchange
    string symbol = 20;
    int64 quantity = 30;
    int64 price_mantissa = 31;      // Price * 10^8 (no float)
    Side side = 33;
    string trader_id = 40;
}

enum Side {
    SIDE_UNSPECIFIED = 0;
    SIDE_BUY = 1;
    SIDE_SELL = 2;
}`
    }
  },
  { 
    layer: 'Tracing', 
    technology: 'OpenTelemetry + Jaeger', 
    icon: '🔗', 
    description: 'Distributed tracing', 
    color: '#60D0E4',
    details: 'Full distributed tracing with OpenTelemetry integration. Auto-instruments ASP.NET Core, HttpClient, Redis, and SQL Server. Custom activity sources for Kafka operations including StartIngestion, StartPnlCalculation, StartRedisPublish, StartBulkInsert, and StartKafkaProduce spans. Jaeger UI provides trace search, service dependency graph, and latency analysis.',
    codeExample: {
      title: 'OpenTelemetry Configuration',
      language: 'csharp',
      file: 'TelemetryExtensions.cs',
      code: `public static IServiceCollection AddEodTelemetry(
    this IServiceCollection services,
    string serviceName)
{
    return services.AddOpenTelemetry()
        .WithTracing(builder => builder
            .AddSource(EodActivitySource.Name)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRedisInstrumentation()
            .AddSqlClientInstrumentation()
            .AddOtlpExporter(opts => 
                opts.Endpoint = new Uri(otlpEndpoint)))
        .WithMetrics(builder => builder
            .AddMeter(EodMetrics.MeterName)
            .AddPrometheusExporter());
}`
    }
  },
  { 
    layer: 'Metrics', 
    technology: 'Prometheus + Grafana', 
    icon: '📊', 
    description: 'Metrics collection & viz', 
    color: '#E6522C',
    details: 'Time-series metrics storage with Prometheus scraping all services. Custom metrics include counters (TradesIngested, TradesProcessed, DlqMessages), histograms (IngestionLatency, PnlCalculationLatency, BulkInsertLatency), and gauges (ConsumerLag, PositionCount). Pre-configured Grafana dashboards: EOD Overview, Services, Kafka, Redis, DLQ, and Tracing.',
    codeExample: {
      title: 'Custom Metrics Implementation',
      language: 'csharp',
      file: 'EodMetrics.cs',
      code: `public static class EodMetrics
{
    public static readonly Meter Meter = new(MeterName);
    
    private static readonly Counter<long> _tradesIngested = 
        Meter.CreateCounter<long>("eod_trades_ingested_total");
    
    private static readonly Histogram<double> _pnlLatency = 
        Meter.CreateHistogram<double>(
            "eod_pnl_calculation_duration_ms",
            unit: "ms",
            description: "P&L calculation latency");

    public static void RecordPnlLatency(double ms) =>
        _pnlLatency.Record(ms, 
            new("service", "flash-pnl"));
}`
    }
  },
  { 
    layer: 'Schema', 
    technology: 'Confluent Schema Registry', 
    icon: '📋', 
    description: 'Schema versioning', 
    color: '#7B68EE',
    details: 'Centralized schema management for Protobuf messages. Provides schema evolution with backward/forward compatibility enforcement, contract validation between producers and consumers, central documentation source for message formats, and debugging capability to decode messages without code. Supports multiple naming strategies: TopicName, RecordName, TopicRecordName.',
    codeExample: {
      title: 'Schema Registry Integration',
      language: 'csharp',
      file: 'SchemaRegistryService.cs',
      code: `public async Task<int> RegisterSchemaAsync<T>(
    string subject) where T : IMessage<T>, new()
{
    var descriptor = new T().Descriptor;
    var schema = new Schema(
        descriptor.File.SerializedData.ToBase64(),
        SchemaType.Protobuf);
    
    var schemaId = await _schemaRegistry
        .RegisterSchemaAsync(subject, schema);
    
    _logger.LogInformation(
        "Registered schema {Subject} with ID {SchemaId}",
        subject, schemaId);
    
    return schemaId;
}`
    }
  },
  { 
    layer: 'Container', 
    technology: 'Docker Compose', 
    icon: '🐳', 
    description: 'Container orchestration', 
    color: '#2496ED',
    details: 'All services containerized with Docker and orchestrated via Docker Compose. Supports scaling (docker compose up -d --scale flash-pnl=3), separate overlay files for burst mode and observability, resource limits configured per service, and easy migration path to ECS/EKS for production. The eod-network Docker bridge provides service discovery.',
    codeExample: {
      title: 'Docker Compose Service Definition',
      language: 'yaml',
      file: 'docker-compose.yml',
      code: `flash-pnl:
  build:
    context: .
    dockerfile: src/Eod.FlashPnl/Dockerfile
  environment:
    - ASPNETCORE_ENVIRONMENT=Docker
    - Kafka__BootstrapServers=kafka:29092
    - Redis__ConnectionString=redis:6379
    - OTEL_EXPORTER_OTLP_ENDPOINT=http://otel:4317
  depends_on:
    kafka: { condition: service_healthy }
    redis: { condition: service_healthy }
  deploy:
    replicas: 1
    resources:
      limits:
        cpus: '2'
        memory: 1G`
    }
  },
];

export const DESIGN_PATTERNS: DesignPattern[] = [
  { name: 'CQRS', implementation: 'Hot Path / Cold Path split', purpose: 'Separate read-optimized (P&L) from write-optimized (Regulatory)', icon: '🔀' },
  { name: 'Circuit Breaker', implementation: 'ICircuitBreaker, CircuitBreakerFactory', purpose: 'Prevent cascade failures when dependencies fail', icon: '🔌' },
  { name: 'Dead Letter Queue', implementation: 'DeadLetterQueueService', purpose: 'Handle unprocessable messages without blocking', icon: '📬' },
  { name: 'Repository', implementation: 'IScenarioRepository', purpose: 'Abstract data access for test scenarios', icon: '📁' },
  { name: 'Factory', implementation: 'IScenarioFactory, ICircuitBreakerFactory', purpose: 'Create configured instances', icon: '🏭' },
  { name: 'Observer', implementation: 'ITestObserver in TestRunner', purpose: 'Notify clients of test progress', icon: '👁️' },
  { name: 'Decorator', implementation: 'ResilientRedisService', purpose: 'Add circuit breaker to Redis operations', icon: '🎀' },
  { name: 'BackgroundService', implementation: 'All processing services', purpose: 'Long-running async processing', icon: '⏳' },
];

export const SERVICES: ServiceInfo[] = [
  // Application Tier
  { name: 'Ingestion', port: '8080', purpose: 'FIX message ingestion and parsing', icon: '🔄', category: 'application' },
  { name: 'Flash P&L', port: '8081', purpose: 'Real-time P&L calculations', icon: '⚡', category: 'application' },
  { name: 'Regulatory', port: '8082', purpose: 'Compliance data persistence', icon: '📋', category: 'application' },
  { name: 'Test Runner', port: '8083', purpose: 'Testing dashboard', icon: '🧪', category: 'application' },
  
  // Infrastructure Tier
  { name: 'Kafka', port: '9092/9093', purpose: 'Event streaming platform', icon: '📨', category: 'infrastructure' },
  { name: 'Schema Registry', port: '8085', purpose: 'Schema management', icon: '📋', category: 'infrastructure' },
  { name: 'Redis', port: '6379', purpose: 'Position cache & pub/sub', icon: '💾', category: 'infrastructure' },
  { name: 'SQL Server', port: '1433', purpose: 'Trade persistence', icon: '🗄️', category: 'infrastructure' },
  { name: 'MinIO', port: '9000/9001', purpose: 'FIX message archive', icon: '📦', category: 'infrastructure' },
  
  // Observability Tier
  { name: 'Prometheus', port: '9090', purpose: 'Metrics collection', icon: '📈', category: 'observability' },
  { name: 'Grafana', port: '3000', purpose: 'Dashboards & visualization', icon: '📊', category: 'observability' },
  { name: 'Jaeger', port: '16686', purpose: 'Distributed tracing', icon: '🔍', category: 'observability' },
  { name: 'OTEL Collector', port: '4316/4319', purpose: 'Telemetry pipeline', icon: '🔗', category: 'observability' },
  { name: 'Kafka Exporter', port: '9308', purpose: 'Kafka metrics export', icon: '📤', category: 'observability' },
  { name: 'Redis Exporter', port: '9121', purpose: 'Redis metrics export', icon: '📤', category: 'observability' },
  { name: 'Kafka UI', port: '8090', purpose: 'Kafka administration', icon: '🖥️', category: 'observability' },
];

export const CIRCUIT_BREAKER_STATES = [
  { state: 'CLOSED', description: 'Normal operation - requests pass through', color: 'var(--status-passed)' },
  { state: 'OPEN', description: 'Fail fast - requests blocked', color: 'var(--status-failed)' },
  { state: 'HALF-OPEN', description: 'Test mode - limited requests allowed', color: 'var(--accent-yellow)' },
];

export const DATA_FLOW_STEPS = [
  { step: 1, name: 'FIX Message Received', service: 'FixSimulator', icon: '📡' },
  { step: 2, name: 'Checksum Validated', service: 'Ingestion', icon: '✅' },
  { step: 3, name: 'Archive to MinIO', service: 'S3Archive', icon: '💾' },
  { step: 4, name: 'Serialize to Protobuf', service: 'Ingestion', icon: '📄' },
  { step: 5, name: 'Publish to Kafka', service: 'Kafka', icon: '📨' },
  { step: 6, name: 'Hot Path: Flash P&L', service: 'FlashPnl', icon: '⚡' },
  { step: 7, name: 'Cold Path: Regulatory', service: 'Regulatory', icon: '❄️' },
  { step: 8, name: 'Update Redis Position', service: 'Redis', icon: '💾' },
  { step: 9, name: 'Bulk Insert SQL', service: 'SQL Server', icon: '🗄️' },
];

export const PATH_COMPARISON = {
  hotPath: {
    name: 'Flash P&L (Speed Path)',
    latency: '~100ms',
    accuracy: '~99%',
    useCase: 'Trader hedging decisions',
    storage: 'In-Memory + Redis',
    color: 'var(--accent-orange)',
  },
  coldPath: {
    name: 'Regulatory (Truth Path)',
    latency: 'Hours acceptable',
    accuracy: '100% mandatory',
    useCase: 'SEC/FINRA compliance',
    storage: 'SQL Server + S3',
    color: 'var(--accent-cyan)',
  },
};

export const TEST_SCENARIOS = [
  { type: 'HealthCheck', purpose: 'Verifies all services are responding', criteria: 'All services respond' },
  { type: 'Throughput', purpose: 'Measures sustainable message rate', criteria: '95%+ of target rate' },
  { type: 'Latency', purpose: 'Measures E2E P&L update time', criteria: 'P95 < 100ms' },
  { type: 'EndToEnd', purpose: 'Verifies complete flow', criteria: '95%+ trades in SQL' },
  { type: 'BurstMode', purpose: 'Simulates EOD spike', criteria: '80%+ of burst rate' },
  { type: 'DataIntegrity', purpose: 'Verifies data correctness', criteria: 'Zero mismatches' },
  { type: 'DeadLetterQueue', purpose: 'Verifies error handling', criteria: 'Invalid messages in DLQ' },
  { type: 'SchemaRegistry', purpose: 'Verifies schema management', criteria: 'Registration + validation' },
];

export const GLOSSARY = [
  { term: 'CQRS', definition: 'Command Query Responsibility Segregation' },
  { term: 'EOD', definition: 'End of Day' },
  { term: 'Flash P&L', definition: 'Quick, approximate profit/loss' },
  { term: 'FIX', definition: 'Financial Information eXchange protocol' },
  { term: 'LTP', definition: 'Last Traded Price' },
  { term: 'DLQ', definition: 'Dead Letter Queue' },
  { term: 'CAT', definition: 'Consolidated Audit Trail (SEC)' },
  { term: 'MPID', definition: 'Market Participant Identifier' },
  { term: 'MOC/LOC', definition: 'Market/Limit On Close orders' },
];
