using Eod.Shared.Configuration;
using Eod.Shared.Kafka;
using Eod.Shared.Redis;
using Eod.Shared.Resilience;
using Eod.Shared.Schema;
using Eod.Shared.Telemetry;
using Eod.TestRunner.Abstractions;
using Eod.TestRunner.Hubs;
using Eod.TestRunner.Models.Metrics;
using Eod.TestRunner.Services;
using Eod.TestRunner.Services.Metrics;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add SignalR for real-time updates
builder.Services.AddSignalR();

// Add CORS for React frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy.WithOrigins("http://localhost:3001", "http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Configure settings
builder.Services.Configure<KafkaSettings>(
    builder.Configuration.GetSection(KafkaSettings.SectionName));
builder.Services.Configure<RedisSettings>(
    builder.Configuration.GetSection(RedisSettings.SectionName));
builder.Services.Configure<SqlServerSettings>(
    builder.Configuration.GetSection(SqlServerSettings.SectionName));
builder.Services.Configure<SchemaRegistrySettings>(
    builder.Configuration.GetSection(SchemaRegistrySettings.SectionName));
builder.Services.Configure<MetricsCollectorSettings>(
    builder.Configuration.GetSection(MetricsCollectorSettings.SectionName));

// Register HTTP client factory
builder.Services.AddHttpClient();

// Register resilience services (Circuit Breaker pattern)
builder.Services.AddSingleton<ICircuitBreakerFactory, CircuitBreakerFactory>();

// Register shared services
builder.Services.AddSingleton<KafkaProducerService>();
builder.Services.AddSingleton<RedisService>();
builder.Services.AddSingleton<SchemaRegistryService>();
builder.Services.AddSingleton<SchemaValidatedProducerService>();

// Register metrics collectors (ISP - each collector has a single responsibility)
builder.Services.AddSingleton<IMetricsCollector<IngestionMetrics>, IngestionMetricsCollector>();
builder.Services.AddSingleton<IMetricsCollector<KafkaMetrics>, KafkaMetricsCollector>();
builder.Services.AddSingleton<IMetricsCollector<FlashPnlMetrics>, FlashPnlMetricsCollector>();
builder.Services.AddSingleton<IMetricsCollector<RegulatoryMetrics>, RegulatoryMetricsCollector>();
builder.Services.AddSingleton<IMetricsCollector<RedisMetrics>, RedisMetricsCollector>();
builder.Services.AddSingleton<IMetricsCollector<SqlServerMetrics>, SqlServerMetricsCollector>();

// Register metrics aggregator (DIP - depends on abstractions)
builder.Services.AddSingleton<ISystemMetricsAggregator, SystemMetricsAggregator>();

// Register scenario services (Repository and Factory patterns)
builder.Services.AddSingleton<IScenarioRepository, ScenarioRepository>();
builder.Services.AddSingleton<IScenarioFactory, ScenarioFactory>();

// Register test execution service
builder.Services.AddSingleton<HealthCheckService>();
builder.Services.AddSingleton<TestExecutionService>();
builder.Services.AddSingleton<ITestExecutionService>(sp => sp.GetRequiredService<TestExecutionService>());

// Register infrastructure initialization service (runs on startup)
builder.Services.AddHostedService<InfrastructureInitializationService>();

// Add health checks
builder.Services.AddHealthChecks();

// Configure OpenTelemetry (tracing, metrics, logging)
builder.Services.AddEodTelemetry("eod-test-runner", "1.0.0");

var app = builder.Build();

// Configure pipeline
app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("AllowReactApp");

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();
app.MapHub<TestHub>("/hubs/test");

// Health check endpoint
app.MapHealthChecks("/health");

// Prometheus metrics endpoint
app.MapPrometheusScrapingEndpoint("/metrics/prometheus");

// Fallback to index.html for SPA routing
app.MapFallbackToFile("index.html");

app.Run();
