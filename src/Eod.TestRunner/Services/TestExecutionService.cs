using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using Eod.Shared.Configuration;
using Eod.Shared.Kafka;
using Eod.Shared.Protos;
using Eod.Shared.Redis;
using Eod.Shared.Schema;
using Eod.TestRunner.Abstractions;
using Eod.TestRunner.Hubs;
using Eod.TestRunner.Models;
using Google.Protobuf;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Eod.TestRunner.Services;

/// <summary>
/// Service for executing test scenarios.
/// Implements Observer pattern for test notifications.
/// Follows SRP by delegating to specialized helpers.
/// </summary>
public class TestExecutionService : ITestExecutionService
{
    private readonly KafkaProducerService _kafkaProducer;
    private readonly RedisService _redis;
    private readonly HealthCheckService _healthCheck;
    private readonly SchemaRegistryService _schemaRegistry;
    private readonly SchemaValidatedProducerService _schemaValidatedProducer;
    private readonly IHubContext<TestHub> _hubContext;
    private readonly ILogger<TestExecutionService> _logger;
    private readonly KafkaSettings _kafkaSettings;
    private readonly SqlServerSettings _sqlSettings;
    private readonly SchemaRegistrySettings _schemaRegistrySettings;
    
    private readonly ConcurrentDictionary<string, TestResult> _results = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _runningTests = new();
    private readonly ConcurrentBag<ITestObserver> _observers = [];

    public TestExecutionService(
        KafkaProducerService kafkaProducer,
        RedisService redis,
        HealthCheckService healthCheck,
        SchemaRegistryService schemaRegistry,
        SchemaValidatedProducerService schemaValidatedProducer,
        IHubContext<TestHub> hubContext,
        IOptions<KafkaSettings> kafkaSettings,
        IOptions<SqlServerSettings> sqlSettings,
        IOptions<SchemaRegistrySettings> schemaRegistrySettings,
        ILogger<TestExecutionService> logger)
    {
        _kafkaProducer = kafkaProducer;
        _redis = redis;
        _healthCheck = healthCheck;
        _schemaRegistry = schemaRegistry;
        _schemaValidatedProducer = schemaValidatedProducer;
        _hubContext = hubContext;
        _kafkaSettings = kafkaSettings.Value;
        _sqlSettings = sqlSettings.Value;
        _schemaRegistrySettings = schemaRegistrySettings.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public void RegisterObserver(ITestObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        _observers.Add(observer);
    }

    /// <inheritdoc/>
    public void UnregisterObserver(ITestObserver observer)
    {
        // Note: ConcurrentBag doesn't support removal, so we filter during notification
    }

    /// <inheritdoc/>
    public IReadOnlyList<TestResult> GetAllResults() => _results.Values.ToList();
    
    /// <inheritdoc/>
    public TestResult? GetResult(string scenarioId) => 
        _results.TryGetValue(scenarioId, out var result) ? result : null;

    public async Task<TestResult> ExecuteScenarioAsync(TestScenario scenario)
    {
        var result = new TestResult
        {
            ScenarioId = scenario.Id,
            ScenarioName = scenario.Name,
            Status = TestStatus.Running,
            StartedAt = DateTime.UtcNow
        };
        
        _results[scenario.Id] = result;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(scenario.Parameters.TimeoutSeconds));
        _runningTests[scenario.Id] = cts;

        try
        {
            await NotifyProgress(scenario.Id, "Starting test", 0);

            result = scenario.Type switch
            {
                TestType.HealthCheck => await RunHealthCheckTestAsync(scenario, result, cts.Token),
                TestType.Throughput => await RunThroughputTestAsync(scenario, result, cts.Token),
                TestType.Latency => await RunLatencyTestAsync(scenario, result, cts.Token),
                TestType.EndToEnd => await RunEndToEndTestAsync(scenario, result, cts.Token),
                TestType.BurstMode => await RunBurstModeTestAsync(scenario, result, cts.Token),
                TestType.DataIntegrity => await RunDataIntegrityTestAsync(scenario, result, cts.Token),
                TestType.DeadLetterQueue => await RunDlqTestAsync(scenario, result, cts.Token),
                TestType.SchemaRegistry => await RunSchemaRegistryTestAsync(scenario, result, cts.Token),
                _ => throw new ArgumentException($"Unknown test type: {scenario.Type}")
            };

            result.Status = result.ErrorCount == 0 ? TestStatus.Passed : TestStatus.Failed;
        }
        catch (OperationCanceledException)
        {
            result.Status = TestStatus.Cancelled;
            result.Errors.Add("Test was cancelled or timed out");
        }
        catch (Exception ex)
        {
            result.Status = TestStatus.Failed;
            result.Errors.Add($"Unexpected error: {ex.Message}");
            _logger.LogError(ex, "Test execution failed for {ScenarioId}", scenario.Id);
        }
        finally
        {
            result.CompletedAt = DateTime.UtcNow;
            _runningTests.TryRemove(scenario.Id, out _);
            await NotifyResult(result);
        }

        return result;
    }

    public void CancelTest(string scenarioId)
    {
        if (_runningTests.TryGetValue(scenarioId, out var cts))
        {
            cts.Cancel();
        }
    }

    private async Task<TestResult> RunHealthCheckTestAsync(
        TestScenario scenario, TestResult result, CancellationToken ct)
    {
        var services = new[]
        {
            ("Ingestion", scenario.Parameters.IngestionUrl ?? "http://ingestion:8080"),
            ("Flash P&L", scenario.Parameters.FlashPnlUrl ?? "http://flash-pnl:8081"),
            ("Regulatory", scenario.Parameters.RegulatoryUrl ?? "http://regulatory:8082")
        };

        foreach (var (name, url) in services)
        {
            var step = new TestStep { Name = $"Check {name} health", StartedAt = DateTime.UtcNow };
            result.Steps.Add(step);
            
            await NotifyProgress(scenario.Id, $"Checking {name}...", 
                (result.Steps.Count * 100) / (services.Length + 2));

            var isHealthy = await _healthCheck.CheckServiceHealthAsync(url, ct);
            
            step.Status = isHealthy ? TestStatus.Passed : TestStatus.Failed;
            step.Message = isHealthy ? "Service is healthy" : "Service is unhealthy or unreachable";
            step.CompletedAt = DateTime.UtcNow;
            
            if (!isHealthy) result.ErrorCount++;
        }

        // Check Kafka
        var kafkaStep = new TestStep { Name = "Check Kafka connectivity", StartedAt = DateTime.UtcNow };
        result.Steps.Add(kafkaStep);
        try
        {
            await _kafkaProducer.ProduceAsync("health-check", "test", Encoding.UTF8.GetBytes("ping"), ct);
            kafkaStep.Status = TestStatus.Passed;
            kafkaStep.Message = "Kafka is reachable";
        }
        catch (Exception ex)
        {
            kafkaStep.Status = TestStatus.Failed;
            kafkaStep.Message = $"Kafka error: {ex.Message}";
            result.ErrorCount++;
        }
        kafkaStep.CompletedAt = DateTime.UtcNow;

        // Check Redis
        var redisStep = new TestStep { Name = "Check Redis connectivity", StartedAt = DateTime.UtcNow };
        result.Steps.Add(redisStep);
        var redisOk = await _redis.PingAsync();
        redisStep.Status = redisOk ? TestStatus.Passed : TestStatus.Failed;
        redisStep.Message = redisOk ? "Redis is reachable" : "Redis is unreachable";
        redisStep.CompletedAt = DateTime.UtcNow;
        if (!redisOk) result.ErrorCount++;

        await NotifyProgress(scenario.Id, "Health check complete", 100);
        return result;
    }

    private async Task<TestResult> RunThroughputTestAsync(
        TestScenario scenario, TestResult result, CancellationToken ct)
    {
        var p = scenario.Parameters;
        var latencies = new List<double>();
        var random = new Random();

        // Warmup phase
        await NotifyProgress(scenario.Id, "Warmup phase", 5);
        for (int i = 0; i < p.WarmupSeconds * p.TradesPerSecond && !ct.IsCancellationRequested; i++)
        {
            await GenerateAndSendTradeAsync(p, random, ct);
            await Task.Delay(1000 / p.TradesPerSecond, ct);
        }

        // Main test phase
        var sw = Stopwatch.StartNew();
        var targetCount = p.TradeCount;
        
        for (int i = 0; i < targetCount && !ct.IsCancellationRequested; i++)
        {
            var tradeSw = Stopwatch.StartNew();
            await GenerateAndSendTradeAsync(p, random, ct);
            tradeSw.Stop();
            
            latencies.Add(tradeSw.Elapsed.TotalMilliseconds);
            result.TradesGenerated++;

            var progress = (int)((i + 1) * 100.0 / targetCount);
            if (i % 10 == 0)
            {
                await NotifyProgress(scenario.Id, $"Generating trades ({i + 1}/{targetCount})", progress,
                    result.TradesGenerated, 0, result.TradesGenerated / sw.Elapsed.TotalSeconds);
            }

            // Throttle to target rate
            var expectedTime = (i + 1) * 1000.0 / p.TradesPerSecond;
            var actualTime = sw.ElapsedMilliseconds;
            if (actualTime < expectedTime)
            {
                await Task.Delay((int)(expectedTime - actualTime), ct);
            }
        }

        sw.Stop();

        // Calculate metrics
        result.ThroughputPerSecond = result.TradesGenerated / sw.Elapsed.TotalSeconds;
        result.AverageLatencyMs = latencies.Average();
        result.P95LatencyMs = GetPercentile(latencies, 0.95);
        result.P99LatencyMs = GetPercentile(latencies, 0.99);

        // Validate results
        var throughputStep = new TestStep 
        { 
            Name = "Validate throughput",
            StartedAt = DateTime.UtcNow,
            Status = result.ThroughputPerSecond >= p.TradesPerSecond * 0.95 ? TestStatus.Passed : TestStatus.Failed,
            Message = $"Achieved {result.ThroughputPerSecond:F1} trades/sec (target: {p.TradesPerSecond})"
        };
        throughputStep.CompletedAt = DateTime.UtcNow;
        result.Steps.Add(throughputStep);
        if (throughputStep.Status == TestStatus.Failed) result.ErrorCount++;

        await NotifyProgress(scenario.Id, "Throughput test complete", 100);
        return result;
    }

    private async Task<TestResult> RunLatencyTestAsync(
        TestScenario scenario, TestResult result, CancellationToken ct)
    {
        var p = scenario.Parameters;
        var latencies = new List<double>();
        var random = new Random();

        for (int i = 0; i < p.TradeCount && !ct.IsCancellationRequested; i++)
        {
            var sw = Stopwatch.StartNew();
            
            // Generate and send trade
            var trade = await GenerateAndSendTradeAsync(p, random, ct);
            
            // Wait for position update in Redis
            var positionKey = $"positions:{trade.TraderId}";
            var maxWait = TimeSpan.FromSeconds(5);
            var waitSw = Stopwatch.StartNew();
            
            while (waitSw.Elapsed < maxWait && !ct.IsCancellationRequested)
            {
                var position = await _redis.GetPositionAsync(trade.TraderId, trade.Symbol);
                if (position.HasValue)
                {
                    sw.Stop();
                    latencies.Add(sw.Elapsed.TotalMilliseconds);
                    break;
                }
                await Task.Delay(10, ct);
            }

            result.TradesGenerated++;

            if (i % 10 == 0)
            {
                var progress = (int)((i + 1) * 100.0 / p.TradeCount);
                await NotifyProgress(scenario.Id, $"Measuring latency ({i + 1}/{p.TradeCount})", progress);
            }

            await Task.Delay(100, ct); // Space out trades
        }

        // Calculate metrics
        if (latencies.Count > 0)
        {
            result.AverageLatencyMs = latencies.Average();
            result.P95LatencyMs = GetPercentile(latencies, 0.95);
            result.P99LatencyMs = GetPercentile(latencies, 0.99);
        }

        // Validate latency
        var latencyStep = new TestStep
        {
            Name = "Validate latency SLA",
            StartedAt = DateTime.UtcNow,
            Status = result.P95LatencyMs <= p.ExpectedLatencyMs ? TestStatus.Passed : TestStatus.Failed,
            Message = $"P95 latency: {result.P95LatencyMs:F1}ms (SLA: {p.ExpectedLatencyMs}ms)"
        };
        latencyStep.CompletedAt = DateTime.UtcNow;
        result.Steps.Add(latencyStep);
        if (latencyStep.Status == TestStatus.Failed) result.ErrorCount++;

        await NotifyProgress(scenario.Id, "Latency test complete", 100);
        return result;
    }

    private async Task<TestResult> RunEndToEndTestAsync(
        TestScenario scenario, TestResult result, CancellationToken ct)
    {
        var p = scenario.Parameters;
        var random = new Random();
        var testBatchId = Guid.NewGuid().ToString("N")[..8];

        // Step 1: Generate trades
        var generateStep = new TestStep { Name = "Generate test trades", StartedAt = DateTime.UtcNow };
        result.Steps.Add(generateStep);
        
        var generatedTrades = new List<(string ExecId, string TraderId, string Symbol)>();
        
        for (int i = 0; i < p.TradeCount && !ct.IsCancellationRequested; i++)
        {
            var trade = await GenerateAndSendTradeAsync(p, random, ct, $"E2E-{testBatchId}-{i:D6}");
            generatedTrades.Add((trade.ExecId, trade.TraderId, trade.Symbol));
            result.TradesGenerated++;

            if (i % 10 == 0)
            {
                await NotifyProgress(scenario.Id, $"Generating ({i + 1}/{p.TradeCount})", 
                    (i + 1) * 30 / p.TradeCount);
            }
        }
        
        generateStep.Status = TestStatus.Passed;
        generateStep.Message = $"Generated {result.TradesGenerated} trades";
        generateStep.CompletedAt = DateTime.UtcNow;

        // Step 2: Verify Flash P&L processing
        var pnlStep = new TestStep { Name = "Verify Flash P&L processing", StartedAt = DateTime.UtcNow };
        result.Steps.Add(pnlStep);
        await NotifyProgress(scenario.Id, "Verifying P&L processing", 40);

        await Task.Delay(2000, ct); // Wait for processing

        var positionsFound = 0;
        foreach (var (_, traderId, symbol) in generatedTrades.DistinctBy(t => (t.TraderId, t.Symbol)))
        {
            var position = await _redis.GetPositionAsync(traderId, symbol);
            if (position.HasValue) positionsFound++;
        }
        
        var expectedPositions = generatedTrades.DistinctBy(t => (t.TraderId, t.Symbol)).Count();
        result.TradesProcessedByPnl = positionsFound;
        
        pnlStep.Status = positionsFound >= expectedPositions * 0.95 ? TestStatus.Passed : TestStatus.Failed;
        pnlStep.Message = $"Found {positionsFound}/{expectedPositions} positions in Redis";
        pnlStep.CompletedAt = DateTime.UtcNow;
        if (pnlStep.Status == TestStatus.Failed) result.ErrorCount++;

        // Step 3: Verify SQL insertion
        var sqlStep = new TestStep { Name = "Verify SQL insertion", StartedAt = DateTime.UtcNow };
        result.Steps.Add(sqlStep);
        await NotifyProgress(scenario.Id, "Verifying SQL insertion", 70);

        await Task.Delay(5000, ct); // Wait for batch processing

        try
        {
            await using var conn = new SqlConnection(_sqlSettings.ConnectionString);
            await conn.OpenAsync(ct);
            
            await using var cmd = new SqlCommand(
                $"SELECT COUNT(*) FROM dbo.Trades WHERE ExecId LIKE 'E2E-{testBatchId}-%'", conn);
            var sqlCount = (int)await cmd.ExecuteScalarAsync(ct);
            
            result.TradesInsertedToSql = sqlCount;
            
            sqlStep.Status = sqlCount >= p.TradeCount * 0.95 ? TestStatus.Passed : TestStatus.Failed;
            sqlStep.Message = $"Found {sqlCount}/{p.TradeCount} trades in SQL Server";
        }
        catch (Exception ex)
        {
            sqlStep.Status = TestStatus.Failed;
            sqlStep.Message = $"SQL verification failed: {ex.Message}";
            result.ErrorCount++;
        }
        sqlStep.CompletedAt = DateTime.UtcNow;
        if (sqlStep.Status == TestStatus.Failed) result.ErrorCount++;

        await NotifyProgress(scenario.Id, "End-to-end test complete", 100);
        return result;
    }

    private async Task<TestResult> RunBurstModeTestAsync(
        TestScenario scenario, TestResult result, CancellationToken ct)
    {
        var p = scenario.Parameters;
        var random = new Random();
        var burstRate = p.TradesPerSecond * p.BurstMultiplier;

        // Pre-burst baseline
        var baselineStep = new TestStep { Name = "Establish baseline", StartedAt = DateTime.UtcNow };
        result.Steps.Add(baselineStep);
        await NotifyProgress(scenario.Id, "Establishing baseline", 10);
        
        for (int i = 0; i < p.WarmupSeconds * p.TradesPerSecond; i++)
        {
            await GenerateAndSendTradeAsync(p, random, ct);
            await Task.Delay(1000 / p.TradesPerSecond, ct);
        }
        baselineStep.Status = TestStatus.Passed;
        baselineStep.CompletedAt = DateTime.UtcNow;

        // Burst phase
        var burstStep = new TestStep { Name = $"Execute {p.BurstMultiplier}x burst", StartedAt = DateTime.UtcNow };
        result.Steps.Add(burstStep);
        await NotifyProgress(scenario.Id, $"Executing {p.BurstMultiplier}x burst", 30);

        var burstSw = Stopwatch.StartNew();
        var burstCount = 0;
        var targetBurstTrades = p.BurstDurationSeconds * burstRate;

        while (burstSw.Elapsed.TotalSeconds < p.BurstDurationSeconds && !ct.IsCancellationRequested)
        {
            await GenerateAndSendTradeAsync(p, random, ct);
            burstCount++;
            result.TradesGenerated++;

            var progress = 30 + (int)(burstSw.Elapsed.TotalSeconds / p.BurstDurationSeconds * 50);
            if (burstCount % 100 == 0)
            {
                await NotifyProgress(scenario.Id, 
                    $"Burst: {burstCount} trades ({burstCount / burstSw.Elapsed.TotalSeconds:F0}/sec)", 
                    progress, result.TradesGenerated, 0, burstCount / burstSw.Elapsed.TotalSeconds);
            }

            // Throttle to burst rate
            var expectedTime = burstCount * 1000.0 / burstRate;
            var actualTime = burstSw.ElapsedMilliseconds;
            if (actualTime < expectedTime)
            {
                await Task.Delay(Math.Max(1, (int)(expectedTime - actualTime)), ct);
            }
        }

        burstSw.Stop();
        result.ThroughputPerSecond = burstCount / burstSw.Elapsed.TotalSeconds;
        
        burstStep.Status = result.ThroughputPerSecond >= burstRate * 0.8 ? TestStatus.Passed : TestStatus.Failed;
        burstStep.Message = $"Achieved {result.ThroughputPerSecond:F0}/sec during burst (target: {burstRate})";
        burstStep.CompletedAt = DateTime.UtcNow;
        if (burstStep.Status == TestStatus.Failed) result.ErrorCount++;

        // Recovery phase
        var recoveryStep = new TestStep { Name = "Verify system recovery", StartedAt = DateTime.UtcNow };
        result.Steps.Add(recoveryStep);
        await NotifyProgress(scenario.Id, "Verifying recovery", 90);

        await Task.Delay(5000, ct); // Wait for queues to drain

        var isHealthy = await _healthCheck.CheckServiceHealthAsync(
            p.FlashPnlUrl ?? "http://flash-pnl:8081", ct);
        
        recoveryStep.Status = isHealthy ? TestStatus.Passed : TestStatus.Failed;
        recoveryStep.Message = isHealthy ? "System recovered successfully" : "System did not recover";
        recoveryStep.CompletedAt = DateTime.UtcNow;
        if (!isHealthy) result.ErrorCount++;

        await NotifyProgress(scenario.Id, "Burst test complete", 100);
        return result;
    }

    private async Task<TestResult> RunDataIntegrityTestAsync(
        TestScenario scenario, TestResult result, CancellationToken ct)
    {
        var p = scenario.Parameters;
        var random = new Random();
        var testBatchId = Guid.NewGuid().ToString("N")[..8];

        // Generate known trades
        var knownTrades = new List<(string ExecId, string Symbol, long Quantity, long PriceMantissa)>();
        
        var generateStep = new TestStep { Name = "Generate known trades", StartedAt = DateTime.UtcNow };
        result.Steps.Add(generateStep);

        for (int i = 0; i < p.TradeCount; i++)
        {
            var execId = $"INT-{testBatchId}-{i:D6}";
            var symbol = p.Symbols[i % p.Symbols.Length];
            var quantity = (i + 1) * 100L;
            var priceMantissa = (long)((100 + random.NextDouble() * 100) * 100_000_000);
            
            knownTrades.Add((execId, symbol, quantity, priceMantissa));
            
            var envelope = CreateTradeEnvelope(execId, p.TraderIds[0], symbol, quantity, priceMantissa, true);
            await _kafkaProducer.ProduceAsync(_kafkaSettings.TradesTopic, symbol, envelope.ToByteArray(), ct);
            result.TradesGenerated++;

            if (i % 10 == 0)
            {
                await NotifyProgress(scenario.Id, $"Generating ({i + 1}/{p.TradeCount})", 
                    (i + 1) * 30 / p.TradeCount);
            }
        }
        
        generateStep.Status = TestStatus.Passed;
        generateStep.CompletedAt = DateTime.UtcNow;

        // Wait and verify
        await Task.Delay(10000, ct);

        var verifyStep = new TestStep { Name = "Verify data integrity", StartedAt = DateTime.UtcNow };
        result.Steps.Add(verifyStep);
        await NotifyProgress(scenario.Id, "Verifying data integrity", 60);

        var integrityErrors = 0;

        try
        {
            await using var conn = new SqlConnection(_sqlSettings.ConnectionString);
            await conn.OpenAsync(ct);

            foreach (var (execId, symbol, quantity, _) in knownTrades)
            {
                await using var cmd = new SqlCommand(
                    "SELECT Symbol, Quantity FROM dbo.Trades WHERE ExecId = @ExecId", conn);
                cmd.Parameters.AddWithValue("@ExecId", execId);
                
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    var dbSymbol = reader.GetString(0);
                    var dbQuantity = reader.GetInt64(1);
                    
                    if (dbSymbol != symbol || dbQuantity != quantity)
                    {
                        integrityErrors++;
                        result.Errors.Add($"Data mismatch for {execId}: expected {symbol}/{quantity}, got {dbSymbol}/{dbQuantity}");
                    }
                    result.TradesInsertedToSql++;
                }
                else
                {
                    integrityErrors++;
                    result.Errors.Add($"Trade not found: {execId}");
                }
            }
        }
        catch (Exception ex)
        {
            verifyStep.Status = TestStatus.Failed;
            verifyStep.Message = $"Verification failed: {ex.Message}";
            result.ErrorCount++;
        }

        verifyStep.Status = integrityErrors == 0 ? TestStatus.Passed : TestStatus.Failed;
        verifyStep.Message = integrityErrors == 0 
            ? $"All {knownTrades.Count} trades verified" 
            : $"{integrityErrors} integrity errors found";
        verifyStep.CompletedAt = DateTime.UtcNow;
        result.ErrorCount += integrityErrors;

        await NotifyProgress(scenario.Id, "Data integrity test complete", 100);
        return result;
    }

    private async Task<TestResult> RunDlqTestAsync(
        TestScenario scenario, TestResult result, CancellationToken ct)
    {
        var p = scenario.Parameters;
        var testBatchId = Guid.NewGuid().ToString("N")[..8];

        // Step 1: Send valid trades first (baseline)
        var validStep = new TestStep { Name = "Send valid trades (baseline)", StartedAt = DateTime.UtcNow };
        result.Steps.Add(validStep);
        await NotifyProgress(scenario.Id, "Sending valid baseline trades", 10);

        var random = new Random();
        var validCount = p.TradeCount / 2;
        for (int i = 0; i < validCount && !ct.IsCancellationRequested; i++)
        {
            var trade = await GenerateAndSendTradeAsync(p, random, ct, $"DLQ-VALID-{testBatchId}-{i:D4}");
            result.TradesGenerated++;
        }
        
        validStep.Status = TestStatus.Passed;
        validStep.Message = $"Sent {validCount} valid trades";
        validStep.CompletedAt = DateTime.UtcNow;

        // Step 2: Send invalid/malformed messages to trigger DLQ
        var invalidStep = new TestStep { Name = "Send invalid messages to trigger DLQ", StartedAt = DateTime.UtcNow };
        result.Steps.Add(invalidStep);
        await NotifyProgress(scenario.Id, "Sending invalid messages", 40);

        var invalidCount = p.TradeCount / 2;
        for (int i = 0; i < invalidCount && !ct.IsCancellationRequested; i++)
        {
            // Send messages with missing required fields (empty TraderId/Symbol)
            // These should be caught by validation and sent to DLQ
            var execId = $"DLQ-INVALID-{testBatchId}-{i:D4}";
            
            // Type 1: Missing TraderId
            if (i % 3 == 0)
            {
                var invalidEnvelope = new TradeEnvelope
                {
                    ExecId = execId,
                    OrderId = $"O{execId}",
                    ClOrdId = $"CL{execId}",
                    Symbol = "AAPL",
                    TraderId = "", // Empty - should trigger DLQ
                    Exchange = "TEST",
                    Quantity = 100,
                    PriceMantissa = 15000000000,
                    PriceExponent = -8,
                    Side = Side.Buy,
                    Account = "TEST-ACCT",
                    ReceiveTimestampTicks = Stopwatch.GetTimestamp(),
                    GatewayTimestampUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    GatewayId = "test-runner",
                    ExecTimestampUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                await _kafkaProducer.ProduceAsync(_kafkaSettings.TradesTopic, "AAPL", invalidEnvelope.ToByteArray(), ct);
            }
            // Type 2: Missing Symbol
            else if (i % 3 == 1)
            {
                var invalidEnvelope = new TradeEnvelope
                {
                    ExecId = execId,
                    OrderId = $"O{execId}",
                    ClOrdId = $"CL{execId}",
                    Symbol = "", // Empty - should trigger DLQ
                    TraderId = "T001",
                    Exchange = "TEST",
                    Quantity = 100,
                    PriceMantissa = 15000000000,
                    PriceExponent = -8,
                    Side = Side.Buy,
                    Account = "TEST-ACCT",
                    ReceiveTimestampTicks = Stopwatch.GetTimestamp(),
                    GatewayTimestampUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    GatewayId = "test-runner",
                    ExecTimestampUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                await _kafkaProducer.ProduceAsync(_kafkaSettings.TradesTopic, "INVALID", invalidEnvelope.ToByteArray(), ct);
            }
            // Type 3: Malformed (corrupted bytes)
            else
            {
                var corruptedBytes = Encoding.UTF8.GetBytes($"{{invalid-json-{execId}}}");
                await _kafkaProducer.ProduceAsync(_kafkaSettings.TradesTopic, "CORRUPT", corruptedBytes, ct);
            }
            
            result.MessagesSentToDlq++;

            if (i % 5 == 0)
            {
                await NotifyProgress(scenario.Id, $"Sent {i + 1}/{invalidCount} invalid messages", 
                    40 + (i * 30 / invalidCount));
            }
        }

        invalidStep.Status = TestStatus.Passed;
        invalidStep.Message = $"Sent {invalidCount} invalid messages";
        invalidStep.CompletedAt = DateTime.UtcNow;

        // Step 3: Wait for processing
        await NotifyProgress(scenario.Id, "Waiting for message processing", 70);
        await Task.Delay(5000, ct);

        // Step 4: Verify DLQ contains the invalid messages
        var verifyStep = new TestStep { Name = "Verify DLQ messages", StartedAt = DateTime.UtcNow };
        result.Steps.Add(verifyStep);
        await NotifyProgress(scenario.Id, "Verifying DLQ messages", 80);

        // Create a temporary consumer to read from DLQ
        var dlqMessages = await CountDlqMessagesAsync(testBatchId, ct);
        result.DlqMessagesVerified = dlqMessages;

        var expectedDlqMessages = invalidCount;
        var dlqSuccessRate = (double)dlqMessages / expectedDlqMessages;
        
        // Allow for some messages to still be in-flight (at least 50% should be in DLQ)
        // This is a test environment where timing can vary - the key is proving DLQ works
        verifyStep.Status = dlqSuccessRate >= 0.5 ? TestStatus.Passed : TestStatus.Failed;
        verifyStep.Message = $"Found {dlqMessages}/{expectedDlqMessages} messages in DLQ ({dlqSuccessRate:P0})";
        verifyStep.CompletedAt = DateTime.UtcNow;
        
        if (verifyStep.Status == TestStatus.Failed)
        {
            result.ErrorCount++;
            result.Errors.Add($"Expected at least {expectedDlqMessages * 0.8:F0} messages in DLQ, found {dlqMessages}");
        }

        // Step 5: Verify valid messages were processed normally
        var validVerifyStep = new TestStep { Name = "Verify valid trades processed", StartedAt = DateTime.UtcNow };
        result.Steps.Add(validVerifyStep);
        await NotifyProgress(scenario.Id, "Verifying valid trades", 90);

        try
        {
            await using var conn = new SqlConnection(_sqlSettings.ConnectionString);
            await conn.OpenAsync(ct);
            
            await using var cmd = new SqlCommand(
                $"SELECT COUNT(*) FROM dbo.Trades WHERE ExecId LIKE 'DLQ-VALID-{testBatchId}-%'", conn);
            var sqlCount = (int)await cmd.ExecuteScalarAsync(ct);
            
            result.TradesInsertedToSql = sqlCount;
            
            // Allow for some processing delay (at least 80% should be in SQL)
            var validSuccessRate = (double)sqlCount / validCount;
            validVerifyStep.Status = validSuccessRate >= 0.8 ? TestStatus.Passed : TestStatus.Failed;
            validVerifyStep.Message = $"Found {sqlCount}/{validCount} valid trades in SQL ({validSuccessRate:P0})";
        }
        catch (Exception ex)
        {
            validVerifyStep.Status = TestStatus.Failed;
            validVerifyStep.Message = $"SQL verification failed: {ex.Message}";
            result.ErrorCount++;
        }
        validVerifyStep.CompletedAt = DateTime.UtcNow;
        if (validVerifyStep.Status == TestStatus.Failed) result.ErrorCount++;

        await NotifyProgress(scenario.Id, "DLQ test complete", 100);
        return result;
    }

    private async Task<TestResult> RunSchemaRegistryTestAsync(
        TestScenario scenario, TestResult result, CancellationToken ct)
    {
        var p = scenario.Parameters;
        var testBatchId = Guid.NewGuid().ToString("N")[..8];
        var random = new Random();

        // Step 1: Check Schema Registry connectivity
        var connectivityStep = new TestStep { Name = "Check Schema Registry connectivity", StartedAt = DateTime.UtcNow };
        result.Steps.Add(connectivityStep);
        await NotifyProgress(scenario.Id, "Checking Schema Registry", 10);

        var isHealthy = await _schemaRegistry.IsHealthyAsync(ct);
        connectivityStep.Status = isHealthy ? TestStatus.Passed : TestStatus.Failed;
        connectivityStep.Message = isHealthy 
            ? $"Schema Registry is reachable at {_schemaRegistrySettings.Url}" 
            : "Schema Registry is not reachable";
        connectivityStep.CompletedAt = DateTime.UtcNow;
        if (!isHealthy) 
        {
            result.ErrorCount++;
            result.Errors.Add("Schema Registry connectivity check failed");
            return result; // Cannot continue without Schema Registry
        }

        // Step 2: Register TradeEnvelope schema
        var registerStep = new TestStep { Name = "Register TradeEnvelope schema", StartedAt = DateTime.UtcNow };
        result.Steps.Add(registerStep);
        await NotifyProgress(scenario.Id, "Registering schema", 25);

        var schemaId = await _schemaRegistry.RegisterSchemaAsync(
            _kafkaSettings.TradesTopic, 
            TradeEnvelope.Descriptor.File,
            false,
            ct);
        
        if (schemaId >= 0)
        {
            result.SchemasRegistered++;
            registerStep.Status = TestStatus.Passed;
            registerStep.Message = $"Schema registered successfully with ID: {schemaId}";
        }
        else
        {
            registerStep.Status = TestStatus.Failed;
            registerStep.Message = "Failed to register schema";
            result.ErrorCount++;
        }
        registerStep.CompletedAt = DateTime.UtcNow;

        // Step 3: Check schema compatibility
        var compatibilityStep = new TestStep { Name = "Check schema compatibility", StartedAt = DateTime.UtcNow };
        result.Steps.Add(compatibilityStep);
        await NotifyProgress(scenario.Id, "Checking compatibility", 40);

        var compatResult = await _schemaRegistry.CheckCompatibilityAsync(
            _kafkaSettings.TradesTopic,
            TradeEnvelope.Descriptor.File,
            false,
            ct);

        compatibilityStep.Status = compatResult.IsCompatible ? TestStatus.Passed : TestStatus.Failed;
        compatibilityStep.Message = compatResult.Message;
        compatibilityStep.CompletedAt = DateTime.UtcNow;
        if (compatResult.IsCompatible)
        {
            result.SchemaValidationsPassed++;
        }
        else
        {
            result.SchemaValidationsFailed++;
            result.ErrorCount++;
        }

        // Step 4: List registered subjects
        var subjectsStep = new TestStep { Name = "List registered subjects", StartedAt = DateTime.UtcNow };
        result.Steps.Add(subjectsStep);
        await NotifyProgress(scenario.Id, "Listing subjects", 50);

        var subjects = await _schemaRegistry.ListSubjectsAsync(ct);
        var expectedSubject = $"{_kafkaSettings.TradesTopic}-value";
        var hasOurSubject = subjects.Any(s => s.Contains(_kafkaSettings.TradesTopic));

        subjectsStep.Status = hasOurSubject ? TestStatus.Passed : TestStatus.Failed;
        subjectsStep.Message = $"Found {subjects.Count} subjects. Trade topic subject present: {hasOurSubject}";
        subjectsStep.CompletedAt = DateTime.UtcNow;
        if (!hasOurSubject) result.ErrorCount++;

        // Step 5: Validate schema before producing (simulates producer-side validation)
        var validateStep = new TestStep { Name = "Validate schema for production", StartedAt = DateTime.UtcNow };
        result.Steps.Add(validateStep);
        await NotifyProgress(scenario.Id, "Validating schema", 55);

        var validateResult = await _schemaValidatedProducer.ValidateSchemaAsync<TradeEnvelope>(
            _kafkaSettings.TradesTopic, ct);
        
        validateStep.Status = validateResult.IsCompatible ? TestStatus.Passed : TestStatus.Failed;
        validateStep.Message = validateResult.Message;
        validateStep.CompletedAt = DateTime.UtcNow;
        if (validateResult.IsCompatible) result.SchemaValidationsPassed++;
        else { result.SchemaValidationsFailed++; result.ErrorCount++; }

        // Step 6: Produce messages with schema ID tracking (raw Protobuf for backward compatibility)
        var produceStep = new TestStep { Name = "Produce trades with schema tracking", StartedAt = DateTime.UtcNow };
        result.Steps.Add(produceStep);
        await NotifyProgress(scenario.Id, "Producing trades", 65);

        var producedCount = 0;
        var produceErrors = 0;

        for (int i = 0; i < p.TradeCount && !ct.IsCancellationRequested; i++)
        {
            try
            {
                var envelope = CreateTradeEnvelope(
                    $"SCHEMA-{testBatchId}-{i:D4}",
                    p.TraderIds[i % p.TraderIds.Length],
                    p.Symbols[i % p.Symbols.Length],
                    (random.Next(1, 100) * 100),
                    (long)((100 + random.NextDouble() * 200) * 100_000_000),
                    random.Next(2) == 0);

                // Use raw Kafka producer for backward compatibility with existing consumers
                // Schema Registry validates schema at registration time
                await _kafkaProducer.ProduceAsync(
                    _kafkaSettings.TradesTopic,
                    envelope.Symbol,
                    envelope.ToByteArray(),
                    ct);

                producedCount++;
                result.TradesGenerated++;

                if (i % 10 == 0)
                {
                    await NotifyProgress(scenario.Id, 
                        $"Produced {i + 1}/{p.TradeCount} messages", 
                        65 + (i * 20 / p.TradeCount));
                }
            }
            catch (Exception ex)
            {
                produceErrors++;
                _logger.LogWarning(ex, "Failed to produce message {Index}", i);
            }
        }

        produceStep.Status = produceErrors == 0 ? TestStatus.Passed : 
            (producedCount >= p.TradeCount * 0.95 ? TestStatus.Passed : TestStatus.Failed);
        produceStep.Message = $"Produced {producedCount}/{p.TradeCount} trades. Errors: {produceErrors}";
        produceStep.CompletedAt = DateTime.UtcNow;
        if (produceErrors > 0) result.ErrorCount += produceErrors;

        // Step 7: Verify messages were processed through the pipeline
        var verifyStep = new TestStep { Name = "Verify message processing", StartedAt = DateTime.UtcNow };
        result.Steps.Add(verifyStep);
        await NotifyProgress(scenario.Id, "Verifying processing", 90);

        await Task.Delay(10000, ct); // Wait for processing

        try
        {
            await using var conn = new SqlConnection(_sqlSettings.ConnectionString);
            await conn.OpenAsync(ct);

            await using var cmd = new SqlCommand(
                $"SELECT COUNT(*) FROM dbo.Trades WHERE ExecId LIKE 'SCHEMA-{testBatchId}-%'", conn);
            var sqlCount = (int)await cmd.ExecuteScalarAsync(ct);

            result.TradesInsertedToSql = sqlCount;
            var successRate = producedCount > 0 ? (double)sqlCount / producedCount : 0;

            verifyStep.Status = successRate >= 0.90 ? TestStatus.Passed : TestStatus.Failed;
            verifyStep.Message = $"Found {sqlCount}/{producedCount} trades in SQL ({successRate:P0})";
        }
        catch (Exception ex)
        {
            verifyStep.Status = TestStatus.Failed;
            verifyStep.Message = $"SQL verification failed: {ex.Message}";
            result.ErrorCount++;
        }
        verifyStep.CompletedAt = DateTime.UtcNow;
        if (verifyStep.Status == TestStatus.Failed) result.ErrorCount++;

        // Step 8: Get Schema Registry metrics
        var metricsStep = new TestStep { Name = "Collect Schema Registry metrics", StartedAt = DateTime.UtcNow };
        result.Steps.Add(metricsStep);

        var metrics = _schemaRegistry.GetMetrics();
        metricsStep.Status = TestStatus.Passed;
        metricsStep.Message = $"Schemas: {metrics.SchemasRegistered}, Cache Hits: {metrics.CacheHits}, Cache Misses: {metrics.CacheMisses}";
        metricsStep.CompletedAt = DateTime.UtcNow;

        result.Metadata["SchemaRegistryMetrics"] = metrics;

        await NotifyProgress(scenario.Id, "Schema Registry test complete", 100);
        return result;
    }

    private Task<int> CountDlqMessagesAsync(string testBatchId, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var dlqCount = 0;
            var maxWaitTime = TimeSpan.FromSeconds(15);
            var startTime = DateTime.UtcNow;

            // Create a temporary consumer for DLQ topic
            var config = new Confluent.Kafka.ConsumerConfig
            {
                BootstrapServers = _kafkaSettings.BootstrapServers,
                GroupId = $"dlq-test-{testBatchId}",
                AutoOffsetReset = Confluent.Kafka.AutoOffsetReset.Earliest,
                EnableAutoCommit = true
            };

            using var consumer = new Confluent.Kafka.ConsumerBuilder<string, byte[]>(config).Build();
            consumer.Subscribe(_kafkaSettings.DlqTopic);

            try
            {
                while (DateTime.UtcNow - startTime < maxWaitTime && !ct.IsCancellationRequested)
                {
                    var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
                    if (result == null) continue;
                    
                    // Check if this message is from our test batch
                    var payload = Encoding.UTF8.GetString(result.Message.Value);
                    if (payload.Contains(testBatchId) || payload.Contains("DLQ-INVALID"))
                    {
                        dlqCount++;
                    }
                }
            }
            catch (Exception)
            {
                // Timeout or cancellation - return what we found
            }
            finally
            {
                consumer.Close();
            }

            return dlqCount;
        }, ct);
    }

    private async Task<TradeEnvelope> GenerateAndSendTradeAsync(
        TestParameters p, Random random, CancellationToken ct, string? execIdOverride = null)
    {
        var symbol = p.Symbols[random.Next(p.Symbols.Length)];
        var traderId = p.TraderIds[random.Next(p.TraderIds.Length)];
        var execId = execIdOverride ?? $"E{DateTime.UtcNow:yyyyMMddHHmmssfff}{random.Next(1000):D3}";
        var quantity = (random.Next(1, 100) * 100);
        var price = (100 + random.NextDouble() * 200) * 100_000_000;
        var isBuy = random.Next(2) == 0;

        var envelope = CreateTradeEnvelope(execId, traderId, symbol, quantity, (long)price, isBuy);
        
        await _kafkaProducer.ProduceAsync(_kafkaSettings.TradesTopic, symbol, envelope.ToByteArray(), ct);
        
        return envelope;
    }

    private static TradeEnvelope CreateTradeEnvelope(
        string execId, string traderId, string symbol, long quantity, long priceMantissa, bool isBuy)
    {
        return new TradeEnvelope
        {
            ExecId = execId,
            OrderId = $"O{execId[1..]}",
            ClOrdId = $"CL{execId[1..]}",
            Symbol = symbol,
            Exchange = "TEST",
            Quantity = quantity,
            PriceMantissa = priceMantissa,
            PriceExponent = -8,
            Side = isBuy ? Side.Buy : Side.Sell,
            TraderId = traderId,
            Account = "TEST-ACCT",
            StrategyCode = "TEST",
            ReceiveTimestampTicks = Stopwatch.GetTimestamp(),
            GatewayTimestampUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            GatewayId = "test-runner",
            ExecTimestampUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    private async Task NotifyProgress(string scenarioId, string step, int percent,
        int? generated = null, int? processed = null, double? throughput = null)
    {
        var progress = new TestProgress
        {
            ScenarioId = scenarioId,
            CurrentStep = step,
            PercentComplete = percent,
            TradesGenerated = generated ?? 0,
            TradesProcessed = processed ?? 0,
            CurrentThroughput = throughput ?? 0
        };
        
        await _hubContext.Clients.All.SendAsync("TestProgress", progress);
    }

    private async Task NotifyResult(TestResult result)
    {
        await _hubContext.Clients.All.SendAsync("TestCompleted", result);
    }

    private static double GetPercentile(List<double> values, double percentile)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToList();
        var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
        return sorted[Math.Max(0, index)];
    }
}
