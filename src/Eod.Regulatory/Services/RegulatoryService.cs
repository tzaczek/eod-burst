using System.Diagnostics;
using Confluent.Kafka;
using Eod.Shared.Configuration;
using Eod.Shared.Health;
using Eod.Shared.Kafka;
using Eod.Shared.Models;
using Eod.Shared.Protos;
using Eod.Shared.Telemetry;
using Microsoft.Extensions.Options;

namespace Eod.Regulatory.Services;

/// <summary>
/// Main regulatory service. Consumes trades from Kafka, enriches with reference data,
/// and bulk inserts to SQL Server.
/// 
/// DLQ INTEGRATION:
/// - Deserialization errors → DLQ (message is malformed)
/// - Missing ExecId → DLQ (required field missing)
/// - Processing errors after 3 retries → DLQ
/// </summary>
public sealed class RegulatoryService : BackgroundService
{
    private readonly KafkaConsumerService _consumer;
    private readonly DeadLetterQueueService _dlq;
    private readonly ReferenceDataService _refData;
    private readonly BulkInsertService _bulkInsert;
    private readonly ServiceHealthCheck _healthCheck;
    private readonly EodActivitySource _activitySource;
    private readonly EodMetrics _metrics;
    private readonly ILogger<RegulatoryService> _logger;
    private readonly KafkaSettings _kafkaSettings;
    private readonly SqlServerSettings _sqlSettings;
    
    private readonly List<EnrichedTrade> _buffer;
    
    // DLQ retry configuration
    private const int MaxRetries = 3;

    public RegulatoryService(
        KafkaConsumerService consumer,
        DeadLetterQueueService dlq,
        ReferenceDataService refData,
        BulkInsertService bulkInsert,
        ServiceHealthCheck healthCheck,
        EodActivitySource activitySource,
        EodMetrics metrics,
        IOptions<KafkaSettings> kafkaSettings,
        IOptions<SqlServerSettings> sqlSettings,
        ILogger<RegulatoryService> logger)
    {
        _consumer = consumer;
        _dlq = dlq;
        _refData = refData;
        _bulkInsert = bulkInsert;
        _healthCheck = healthCheck;
        _activitySource = activitySource;
        _metrics = metrics;
        _kafkaSettings = kafkaSettings.Value;
        _sqlSettings = sqlSettings.Value;
        _logger = logger;
        
        _buffer = new List<EnrichedTrade>(_sqlSettings.BulkBatchSize);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Regulatory service starting...");

        try
        {
            // Initialize reference data
            await _refData.InitializeAsync();
            
            // Note: Database schema is now managed by Entity Framework migrations
            // via DatabaseMigrationService which runs on startup
            
            // Subscribe to trades topic
            _consumer.Subscribe(_kafkaSettings.TradesTopic);
            
            _healthCheck.SetReady("Regulatory service ready");
            _logger.LogInformation("Regulatory service is ready");

            var processedCount = 0L;
            var lastLogTime = DateTime.UtcNow;
            var lastFlushTime = DateTime.UtcNow;

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
                        _metrics.IncrementDlqMessages("regulatory", "deserialization");
                        _consumer.Commit(result);
                        continue;
                    }
                    
                    // Enrich trade with DLQ support
                    var enriched = await EnrichTradeWithDlqAsync(result, envelope);
                    if (enriched != null)
                    {
                        _buffer.Add(enriched);
                        processedCount++;
                    }

                    // Flush when buffer is full or time elapsed
                    var shouldFlush = _buffer.Count >= _sqlSettings.BulkBatchSize ||
                        (DateTime.UtcNow - lastFlushTime).TotalSeconds >= 5;

                    if (shouldFlush && _buffer.Count > 0)
                    {
                        await FlushBufferAsync(stoppingToken);
                        _consumer.Commit(result);
                        lastFlushTime = DateTime.UtcNow;
                    }

                    // Log throughput every 30 seconds
                    if ((DateTime.UtcNow - lastLogTime).TotalSeconds >= 30)
                    {
                        var lag = _consumer.GetTotalLag();
                        _logger.LogInformation(
                            "Processed {Count} trades. Inserted: {Inserted}, DLQ: {DLQ}, Errors: {Errors}, Lag: {Lag}",
                            processedCount,
                            _bulkInsert.TotalInserted,
                            _dlq.MessagesEnqueued,
                            _bulkInsert.TotalErrors,
                            lag);
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
            _logger.LogInformation("Regulatory service stopping...");
        }
        finally
        {
            // Final flush
            if (_buffer.Count > 0)
            {
                await FlushBufferAsync(CancellationToken.None);
            }
            
            _consumer.CommitAll();
            _healthCheck.SetNotReady("Service shutting down");
        }
    }

    private async Task<EnrichedTrade?> EnrichTradeWithDlqAsync(
        ConsumeResult<string, byte[]> result,
        TradeEnvelope envelope)
    {
        // Validate required fields - send to DLQ if invalid
        if (string.IsNullOrEmpty(envelope.ExecId))
        {
            _logger.LogWarning("Trade missing ExecId, sending to DLQ");
            
            await _dlq.SendValidationErrorAsync(
                result,
                "Missing required field: ExecId",
                new Dictionary<string, string>
                {
                    ["Symbol"] = envelope.Symbol ?? "null",
                    ["TraderId"] = envelope.TraderId ?? "null"
                });
            
            _metrics.IncrementDlqMessages("regulatory", "validation");
            return null;
        }

        // Try enrichment with retries
        for (int retry = 0; retry <= MaxRetries; retry++)
        {
            try
            {
                return await EnrichTradeAsync(envelope);
            }
            catch (Exception ex) when (retry < MaxRetries)
            {
                _logger.LogWarning(ex,
                    "Trade enrichment failed, retrying ({Retry}/{MaxRetries}). ExecId: {ExecId}",
                    retry + 1, MaxRetries, envelope.ExecId);
                await Task.Delay(100 * (retry + 1)); // Exponential backoff
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Trade enrichment failed after {MaxRetries} retries, sending to DLQ. ExecId: {ExecId}",
                    MaxRetries, envelope.ExecId);

                await _dlq.SendToDeadLetterQueueAsync(
                    result,
                    ex,
                    MaxRetries,
                    new Dictionary<string, string>
                    {
                        ["ExecId"] = envelope.ExecId,
                        ["Symbol"] = envelope.Symbol ?? "unknown",
                        ["TraderId"] = envelope.TraderId ?? "unknown"
                    });

                _metrics.IncrementDlqMessages("regulatory", "processing");
                return null;
            }
        }
        return null;
    }

    private async Task<EnrichedTrade?> EnrichTradeAsync(TradeEnvelope envelope)
    {
        var startTime = Stopwatch.GetTimestamp();

        // Start enrichment trace
        using var activity = _activitySource.StartEnrichment(envelope.ExecId);

        // Look up reference data (these can be cached/fast)
        var trader = await _refData.GetTraderAsync(envelope.TraderId);
        var strategy = await _refData.GetStrategyAsync(envelope.StrategyCode);
        var security = await _refData.GetSecurityAsync(envelope.Symbol);
        
        // Record enrichment latency
        var elapsedMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
        _metrics.RecordEnrichmentLatency(elapsedMs);
        activity?.SetTag("enrichment.latency_ms", elapsedMs);

        // Convert price from mantissa
        var price = envelope.PriceMantissa / 100_000_000m;

        // Map side enum to string
        var side = envelope.Side switch
        {
            Side.Buy => "BUY",
            Side.Sell => "SELL",
            Side.SellShort => "SELL_SHORT",
            Side.SellShortExempt => "SELL_SHORT_EXEMPT",
            _ => "UNKNOWN"
        };

        return new EnrichedTrade
        {
            ExecId = envelope.ExecId,
            Symbol = envelope.Symbol,
            Quantity = envelope.Quantity,
            Price = price,
            Side = side,
            ExecTimestampUtc = DateTimeOffset.FromUnixTimeMilliseconds(envelope.ExecTimestampUtc).UtcDateTime,
            OrderId = envelope.OrderId ?? "",
            ClOrdId = envelope.ClOrdId ?? "",
            TraderId = envelope.TraderId ?? "",
            TraderName = trader?.Name,
            TraderMpid = trader?.Mpid,
            TraderCrd = trader?.Crd,
            Account = envelope.Account ?? "",
            StrategyCode = envelope.StrategyCode,
            StrategyName = strategy?.Name,
            StrategyType = strategy?.Type,
            Cusip = security?.Cusip,
            Sedol = security?.Sedol,
            Isin = security?.Isin,
            SecurityName = security?.Name,
            Exchange = envelope.Exchange ?? "",
            Mic = GetMic(envelope.Exchange),
            SourceGatewayId = envelope.GatewayId ?? "",
            ReceiveTimestampUtc = new DateTime(envelope.ReceiveTimestampTicks, DateTimeKind.Utc),
            EnrichmentTimestampUtc = DateTime.UtcNow,
            RawFixMessage = envelope.RawFix.ToByteArray()
        };
    }

    private async Task FlushBufferAsync(CancellationToken ct)
    {
        if (_buffer.Count == 0)
            return;

        var startTime = Stopwatch.GetTimestamp();
        
        try
        {
            using var activity = _activitySource.StartBulkInsert(_buffer.Count);
            
            var inserted = await _bulkInsert.BulkInsertAsync(_buffer, ct);
            
            var elapsedMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            _metrics.IncrementTradesInserted(inserted);
            _metrics.RecordBulkInsertLatency(elapsedMs, inserted);
            
            activity?.SetTag("batch.inserted", inserted);
            activity?.SetTag("batch.latency_ms", elapsedMs);
            
            _logger.LogDebug("Flushed {Count} trades to SQL Server in {Elapsed:F1}ms", inserted, elapsedMs);
        }
        catch (Exception ex)
        {
            _metrics.IncrementErrors("bulk_insert_failed", "regulatory");
            _logger.LogError(ex, "Failed to flush {Count} trades", _buffer.Count);
        }
        finally
        {
            _buffer.Clear();
        }
    }

    private static string? GetMic(string? exchange) => exchange?.ToUpperInvariant() switch
    {
        "NYSE" => "XNYS",
        "NASDAQ" => "XNAS",
        "ARCA" => "ARCX",
        "BATS" => "BATS",
        "IEX" => "IEXG",
        _ => null
    };
}
