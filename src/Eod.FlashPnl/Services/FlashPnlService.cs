using System.Diagnostics;
using Confluent.Kafka;
using Eod.Shared.Configuration;
using Eod.Shared.Health;
using Eod.Shared.Kafka;
using Eod.Shared.Models;
using Eod.Shared.Protos;
using Eod.Shared.Resilience;
using Eod.Shared.Telemetry;
using Microsoft.Extensions.Options;

namespace Eod.FlashPnl.Services;

/// <summary>
/// Main Flash P&L service. Consumes trades from Kafka, updates in-memory positions,
/// and publishes P&L updates to Redis with circuit breaker protection.
/// 
/// RESILIENCE DESIGN:
/// This service implements the "Process Locally, Publish Optimistically" pattern:
/// 1. Trade processing (in-memory) - ALWAYS succeeds, no I/O
/// 2. Position update (in-memory) - ALWAYS succeeds, no I/O  
/// 3. Redis publish (with circuit breaker) - Best effort, failures don't block processing
/// 
/// DLQ INTEGRATION:
/// - Deserialization errors → DLQ (message is malformed)
/// - Validation errors → DLQ (required fields missing)
/// - Processing errors after 3 retries → DLQ
/// </summary>
public sealed class FlashPnlService : BackgroundService
{
    private readonly KafkaConsumerService _consumer;
    private readonly ResilientRedisService _redis;
    private readonly DeadLetterQueueService _dlq;
    private readonly PositionStore _positions;
    private readonly PriceService _priceService;
    private readonly ServiceHealthCheck _healthCheck;
    private readonly EodActivitySource _activitySource;
    private readonly EodMetrics _metrics;
    private readonly ILogger<FlashPnlService> _logger;
    private readonly KafkaSettings _kafkaSettings;
    
    // Throttle Redis publishes to avoid overwhelming subscribers
    private readonly Dictionary<PositionKey, DateTime> _lastPublishTime = new();
    private static readonly TimeSpan PublishThrottle = TimeSpan.FromMilliseconds(100);
    
    // Track circuit breaker impact
    private long _skippedPublishes;
    
    // DLQ retry configuration
    private const int MaxRetries = 3;

    public FlashPnlService(
        KafkaConsumerService consumer,
        ResilientRedisService redis,
        DeadLetterQueueService dlq,
        PositionStore positions,
        PriceService priceService,
        ServiceHealthCheck healthCheck,
        EodActivitySource activitySource,
        EodMetrics metrics,
        IOptions<KafkaSettings> kafkaSettings,
        ILogger<FlashPnlService> logger)
    {
        _consumer = consumer;
        _redis = redis;
        _dlq = dlq;
        _positions = positions;
        _priceService = priceService;
        _healthCheck = healthCheck;
        _activitySource = activitySource;
        _metrics = metrics;
        _kafkaSettings = kafkaSettings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Flash P&L service starting...");

        try
        {
            // Wait for Redis (circuit breaker will handle ongoing issues)
            await WaitForRedisAsync(stoppingToken);
            
            // Subscribe to trades topic
            _consumer.Subscribe(_kafkaSettings.TradesTopic);
            
            _healthCheck.SetReady("Flash P&L service ready");
            _logger.LogInformation("Flash P&L service is ready");

            var processedCount = 0L;
            var lastLogTime = DateTime.UtcNow;
            var batchStartTime = Stopwatch.GetTimestamp();
            var batchCount = 0;

            await foreach (var result in _consumer.ConsumeAsync(stoppingToken))
            {
                try
                {
                    // Deserialize protobuf - send to DLQ on deserialization errors
                    TradeEnvelope envelope;
                    try
                    {
                        envelope = TradeEnvelope.Parser.ParseFrom(result.Message.Value);
                    }
                    catch (Exception deserEx)
                    {
                        _logger.LogWarning(deserEx, 
                            "Failed to deserialize trade message, sending to DLQ");
                        await _dlq.SendToDeadLetterQueueAsync(
                            result, 
                            DlqReason.DeserializationError,
                            deserEx.Message);
                        _metrics.IncrementDlqMessages("flash-pnl", "deserialization");
                        _consumer.Commit(result);
                        continue;
                    }
                    
                    // Process trade with retry/DLQ logic
                    var processed = await ProcessTradeWithDlqAsync(result, envelope);
                    
                    if (processed)
                    {
                        processedCount++;
                    }
                    batchCount++;

                    // Commit every 100 messages or after 1 second
                    if (batchCount >= 100 || 
                        Stopwatch.GetElapsedTime(batchStartTime) > TimeSpan.FromSeconds(1))
                    {
                        _consumer.Commit(result);
                        batchCount = 0;
                        batchStartTime = Stopwatch.GetTimestamp();
                    }

                    // Log throughput and circuit breaker status every 10 seconds
                    if ((DateTime.UtcNow - lastLogTime).TotalSeconds >= 10)
                    {
                        var lag = _consumer.GetTotalLag();
                        _metrics.SetConsumerLag(lag);
                        _metrics.SetPositionCount(_positions.GetPositionCount());
                        _metrics.SetTraderCount(_positions.GetTraderCount());
                        
                        LogStatusWithCircuitBreaker(processedCount, lag);
                        lastLogTime = DateTime.UtcNow;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error processing trade message");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Flash P&L service stopping...");
        }
        finally
        {
            _consumer.CommitAll();
            _healthCheck.SetNotReady("Service shutting down");
        }
    }

    private async Task<bool> ProcessTradeWithDlqAsync(
        ConsumeResult<string, byte[]> result, 
        TradeEnvelope envelope)
    {
        // Validate required fields - send to DLQ if invalid
        if (string.IsNullOrEmpty(envelope.TraderId) || string.IsNullOrEmpty(envelope.Symbol))
        {
            _logger.LogWarning(
                "Trade missing required fields, sending to DLQ. ExecId: {ExecId}",
                envelope.ExecId);
            
            await _dlq.SendValidationErrorAsync(
                result,
                $"Missing required fields: TraderId={envelope.TraderId}, Symbol={envelope.Symbol}",
                new Dictionary<string, string> { ["ExecId"] = envelope.ExecId ?? "null" });
            
            _metrics.IncrementDlqMessages("flash-pnl", "validation");
            return false;
        }

        // Try processing with retries
        for (int retry = 0; retry <= MaxRetries; retry++)
        {
            try
            {
                await ProcessTradeAsync(envelope);
                return true;
            }
            catch (Exception ex) when (retry < MaxRetries)
            {
                _logger.LogWarning(ex, 
                    "Trade processing failed, retrying ({Retry}/{MaxRetries}). ExecId: {ExecId}",
                    retry + 1, MaxRetries, envelope.ExecId);
                await Task.Delay(100 * (retry + 1)); // Exponential backoff
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Trade processing failed after {MaxRetries} retries, sending to DLQ. ExecId: {ExecId}",
                    MaxRetries, envelope.ExecId);
                
                await _dlq.SendToDeadLetterQueueAsync(
                    result, 
                    ex, 
                    MaxRetries,
                    new Dictionary<string, string>
                    {
                        ["ExecId"] = envelope.ExecId ?? "unknown",
                        ["TraderId"] = envelope.TraderId,
                        ["Symbol"] = envelope.Symbol
                    });
                
                _metrics.IncrementDlqMessages("flash-pnl", "processing");
                return false;
            }
        }
        return false;
    }

    private async Task ProcessTradeAsync(TradeEnvelope envelope)
    {
        var startTime = Stopwatch.GetTimestamp();

        // Start P&L calculation trace
        using var activity = _activitySource.StartPnlCalculation(envelope.TraderId, envelope.Symbol);
        activity?.SetTag("trade.exec_id", envelope.ExecId);

        var isBuy = envelope.Side is Side.Buy;
        
        // STEP 1: Update in-memory position (nanoseconds, no I/O - ALWAYS succeeds)
        var position = _positions.ApplyTrade(
            envelope.TraderId,
            envelope.Symbol,
            envelope.Quantity,
            envelope.PriceMantissa,
            isBuy,
            envelope.ReceiveTimestampTicks);

        // STEP 2: Update LTP from this trade (circuit breaker protected, fire-and-forget)
        _ = _priceService.SetLastTradePriceAsync(envelope.Symbol, envelope.PriceMantissa);

        // STEP 3: Get current mark price for P&L calculation (from local cache - fast)
        var (markPrice, markSource) = _priceService.GetMarkFast(envelope.Symbol);
        
        // Create snapshot for publishing
        var snapshot = position.ToSnapshot(markPrice, markSource);

        // STEP 4: Publish to Redis (circuit breaker protected)
        var key = new PositionKey(envelope.TraderId, envelope.Symbol);
        if (ShouldPublish(key))
        {
            using (_activitySource.StartRedisPublish($"pnl-updates:{envelope.TraderId}"))
            {
                var published = await _redis.UpdateAndPublishAsync(snapshot);
                
                if (published)
                {
                    _lastPublishTime[key] = DateTime.UtcNow;
                }
                else
                {
                    Interlocked.Increment(ref _skippedPublishes);
                    activity?.SetTag("redis.publish_skipped", true);
                }
            }
        }
        
        // Record metrics
        var elapsedMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
        _metrics.IncrementTradesProcessed(envelope.TraderId, envelope.Symbol);
        _metrics.RecordPnlCalculationLatency(elapsedMs);
        
        activity?.SetTag("processing.latency_ms", elapsedMs);
        activity?.SetTag("position.quantity", position.Quantity);
        activity?.SetTag("circuit_breaker.publish_state", _redis.PublishCircuitState.ToString());
    }

    private bool ShouldPublish(PositionKey key)
    {
        if (!_lastPublishTime.TryGetValue(key, out var lastTime))
            return true;
        
        return DateTime.UtcNow - lastTime >= PublishThrottle;
    }

    private async Task WaitForRedisAsync(CancellationToken ct)
    {
        var retryCount = 0;
        while (!ct.IsCancellationRequested && retryCount < 30)
        {
            if (await _redis.PingAsync())
            {
                return;
            }
            
            retryCount++;
            _logger.LogWarning("Waiting for Redis... attempt {Count}/30", retryCount);
            await Task.Delay(2000, ct);
        }
        
        // Continue even if Redis is not available - circuit breaker will handle it
        _logger.LogWarning(
            "Redis not available after 30 attempts. Continuing with circuit breaker protection.");
    }

    private void LogStatusWithCircuitBreaker(long processedCount, long lag)
    {
        var publishState = _redis.PublishCircuitState;
        var queryState = _redis.QueryCircuitState;
        
        // Include circuit breaker status in logs
        var cbStatus = (publishState, queryState) switch
        {
            (CircuitBreakerState.Closed, CircuitBreakerState.Closed) => "",
            (CircuitBreakerState.Open, _) => " [PUBLISH CIRCUIT OPEN]",
            (_, CircuitBreakerState.Open) => " [QUERY CIRCUIT OPEN]",
            (CircuitBreakerState.HalfOpen, _) => " [PUBLISH CIRCUIT HALF-OPEN]",
            (_, CircuitBreakerState.HalfOpen) => " [QUERY CIRCUIT HALF-OPEN]",
            _ => ""
        };

        _logger.LogInformation(
            "Processed {Count} trades. Positions: {Positions}, Traders: {Traders}, Lag: {Lag}, " +
            "Skipped Publishes: {Skipped}{CircuitStatus}",
            processedCount,
            _positions.GetPositionCount(),
            _positions.GetTraderCount(),
            lag,
            Interlocked.Read(ref _skippedPublishes),
            cbStatus);
    }
}
