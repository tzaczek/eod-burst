using System.Diagnostics;
using System.Threading.Channels;
using Eod.Shared.Configuration;
using Eod.Shared.Health;
using Eod.Shared.Kafka;
using Eod.Shared.Protos;
using Eod.Shared.Telemetry;
using Google.Protobuf;
using Microsoft.Extensions.Options;

namespace Eod.Ingestion.Services;

/// <summary>
/// Main ingestion service that receives FIX messages, archives them to S3,
/// and publishes to Kafka.
/// </summary>
public sealed class IngestionService : BackgroundService
{
    private readonly KafkaProducerService _producer;
    private readonly S3ArchiveService _archiver;
    private readonly ServiceHealthCheck _healthCheck;
    private readonly EodActivitySource _activitySource;
    private readonly EodMetrics _metrics;
    private readonly ILogger<IngestionService> _logger;
    private readonly KafkaSettings _kafkaSettings;
    private readonly MessageChannel _messageChannel;
    
    public IngestionService(
        KafkaProducerService producer,
        S3ArchiveService archiver,
        ServiceHealthCheck healthCheck,
        MessageChannel messageChannel,
        EodActivitySource activitySource,
        EodMetrics metrics,
        IOptions<KafkaSettings> kafkaSettings,
        ILogger<IngestionService> logger)
    {
        _producer = producer;
        _archiver = archiver;
        _healthCheck = healthCheck;
        _messageChannel = messageChannel;
        _activitySource = activitySource;
        _metrics = metrics;
        _kafkaSettings = kafkaSettings.Value;
        _logger = logger;
    }

    /// <summary>
    /// External entry point for receiving raw FIX messages.
    /// Called by FixGateway when messages arrive.
    /// </summary>
    public ValueTask<bool> EnqueueAsync(RawFixMessage message, CancellationToken ct = default)
    {
        return _messageChannel.Writer.WaitToWriteAsync(ct).IsCompletedSuccessfully
            ? new ValueTask<bool>(_messageChannel.Writer.TryWrite(message))
            : EnqueueSlowAsync(message, ct);
    }

    private async ValueTask<bool> EnqueueSlowAsync(RawFixMessage message, CancellationToken ct)
    {
        while (await _messageChannel.Writer.WaitToWriteAsync(ct))
        {
            if (_messageChannel.Writer.TryWrite(message))
                return true;
        }
        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Ingestion service starting...");
        
        try
        {
            // Wait for dependencies
            await WaitForKafkaAsync(stoppingToken);
            
            _healthCheck.SetReady("Ingestion service ready");
            _logger.LogInformation("Ingestion service is ready");

            var processedCount = 0L;
            var lastLogTime = DateTime.UtcNow;

            await foreach (var message in _messageChannel.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ProcessMessageAsync(message, stoppingToken);
                    processedCount++;
                    
                    // Log throughput every 10 seconds
                    if ((DateTime.UtcNow - lastLogTime).TotalSeconds >= 10)
                    {
                        _logger.LogInformation(
                            "Processed {Count} messages. Channel depth: {Depth}",
                            processedCount,
                            _messageChannel.Count);
                        lastLogTime = DateTime.UtcNow;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error processing message");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Ingestion service stopping...");
        }
        finally
        {
            _producer.Flush(TimeSpan.FromSeconds(30));
            _healthCheck.SetNotReady("Service shutting down");
        }
    }

    private Task ProcessMessageAsync(RawFixMessage message, CancellationToken ct)
    {
        var startTime = Stopwatch.GetTimestamp();
        
        // STEP 1: Validate FIX checksum (fast, no parsing)
        if (!ValidateChecksum(message.RawBytes))
        {
            _logger.LogWarning("Invalid FIX checksum, dropping message");
            _metrics.IncrementErrors("invalid_checksum", "ingestion");
            return Task.CompletedTask;
        }

        // STEP 2: Archive raw bytes to S3 (fire-and-forget)
        _ = _archiver.ArchiveAsync(message.RawBytes, message.ReceiveTimestamp, ct);

        // STEP 3: Parse minimal fields for partitioning
        var parsed = ParseMinimalFields(message.RawBytes);
        
        // Start distributed trace
        using var activity = _activitySource.StartIngestion(parsed.ExecId, parsed.Symbol);

        // STEP 4: Create protobuf envelope
        var envelope = new TradeEnvelope
        {
            RawFix = ByteString.CopyFrom(message.RawBytes),
            ReceiveTimestampTicks = message.ReceiveTimestamp,
            GatewayTimestampUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            GatewayId = Environment.MachineName,
            
            // Parsed fields
            ExecId = parsed.ExecId,
            OrderId = parsed.OrderId,
            ClOrdId = parsed.ClOrdId,
            Symbol = parsed.Symbol,
            Exchange = parsed.Exchange,
            Quantity = parsed.Quantity,
            PriceMantissa = parsed.PriceMantissa,
            PriceExponent = -8,
            Side = parsed.Side,
            TraderId = parsed.TraderId,
            Account = parsed.Account,
            StrategyCode = parsed.StrategyCode,
            ExecTimestampUtc = parsed.ExecTimestamp,
            FixMsgType = parsed.MsgType,
            FixSeqNum = parsed.SeqNum,
            FixSender = parsed.Sender,
            FixTarget = parsed.Target
        };

        // STEP 5: Publish to Kafka (partitioned by symbol)
        using (var kafkaActivity = _activitySource.StartKafkaProduce(_kafkaSettings.TradesTopic, parsed.Symbol))
        {
            _producer.Produce(
                _kafkaSettings.TradesTopic,
                parsed.Symbol,  // Partition key
                envelope.ToByteArray());
        }
        
        // Record metrics
        var elapsedMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
        _metrics.IncrementTradesIngested(parsed.Symbol);
        _metrics.RecordIngestionLatency(elapsedMs, parsed.Symbol);
        
        activity?.SetTag("processing.latency_ms", elapsedMs);
        
        return Task.CompletedTask;
    }

    private static bool ValidateChecksum(ReadOnlySpan<byte> message)
    {
        // FIX checksum is last 7 bytes: "10=XXX|" 
        // where XXX is sum of all bytes before "10=" mod 256
        
        if (message.Length < 10)
            return false;

        // Find "10=" tag
        var checksumTagIndex = message.LastIndexOf("10="u8);
        if (checksumTagIndex < 0)
            return false;

        // Calculate expected checksum
        var sum = 0;
        for (var i = 0; i < checksumTagIndex; i++)
        {
            sum += message[i];
        }
        var expectedChecksum = sum % 256;

        // Parse actual checksum from message
        var checksumStr = System.Text.Encoding.ASCII.GetString(
            message.Slice(checksumTagIndex + 3, 3));
        
        if (!int.TryParse(checksumStr, out var actualChecksum))
            return false;

        return expectedChecksum == actualChecksum;
    }

    private static ParsedFields ParseMinimalFields(ReadOnlySpan<byte> message)
    {
        // Simplified FIX parsing - extract only fields needed for routing
        // In production, use a proper FIX parser library
        
        var result = new ParsedFields();
        var msgStr = System.Text.Encoding.ASCII.GetString(message);
        var fields = msgStr.Split('\x01'); // SOH delimiter
        
        foreach (var field in fields)
        {
            var eqIndex = field.IndexOf('=');
            if (eqIndex < 0) continue;
            
            var tag = field.AsSpan(0, eqIndex);
            var value = field.AsSpan(eqIndex + 1);
            
            // Parse by FIX tag number
            if (tag.SequenceEqual("35")) result.MsgType = value.ToString();
            else if (tag.SequenceEqual("17")) result.ExecId = value.ToString();
            else if (tag.SequenceEqual("37")) result.OrderId = value.ToString();
            else if (tag.SequenceEqual("11")) result.ClOrdId = value.ToString();
            else if (tag.SequenceEqual("55")) result.Symbol = value.ToString();
            else if (tag.SequenceEqual("207")) result.Exchange = value.ToString();
            else if (tag.SequenceEqual("38") && long.TryParse(value, out var qty)) result.Quantity = qty;
            else if (tag.SequenceEqual("44") && decimal.TryParse(value, out var price)) 
                result.PriceMantissa = (long)(price * 100_000_000);
            else if (tag.SequenceEqual("54")) result.Side = ParseSide(value);
            else if (tag.SequenceEqual("49")) result.Sender = value.ToString();
            else if (tag.SequenceEqual("56")) result.Target = value.ToString();
            else if (tag.SequenceEqual("34") && int.TryParse(value, out var seq)) result.SeqNum = seq;
            else if (tag.SequenceEqual("1")) result.Account = value.ToString();
            // Custom tags for trader/strategy
            else if (tag.SequenceEqual("5001")) result.TraderId = value.ToString();
            else if (tag.SequenceEqual("5002")) result.StrategyCode = value.ToString();
        }
        
        return result;
    }

    private static Side ParseSide(ReadOnlySpan<char> value) => value switch
    {
        "1" => Side.Buy,
        "2" => Side.Sell,
        "5" => Side.SellShort,
        "6" => Side.SellShortExempt,
        _ => Side.Unspecified
    };

    private async Task WaitForKafkaAsync(CancellationToken ct)
    {
        var retryCount = 0;
        while (!ct.IsCancellationRequested && retryCount < 30)
        {
            try
            {
                // Test produce
                await _producer.ProduceAsync(
                    _kafkaSettings.TradesTopic,
                    "health-check",
                    System.Text.Encoding.UTF8.GetBytes("ping"),
                    ct);
                return;
            }
            catch (Exception ex)
            {
                retryCount++;
                _logger.LogWarning(ex, 
                    "Waiting for Kafka... attempt {Count}/30", retryCount);
                await Task.Delay(2000, ct);
            }
        }
        
        throw new InvalidOperationException("Could not connect to Kafka");
    }

    private sealed class ParsedFields
    {
        public string MsgType { get; set; } = "";
        public string ExecId { get; set; } = "";
        public string OrderId { get; set; } = "";
        public string ClOrdId { get; set; } = "";
        public string Symbol { get; set; } = "";
        public string Exchange { get; set; } = "";
        public long Quantity { get; set; }
        public long PriceMantissa { get; set; }
        public Side Side { get; set; }
        public string TraderId { get; set; } = "";
        public string Account { get; set; } = "";
        public string StrategyCode { get; set; } = "";
        public long ExecTimestamp { get; set; }
        public int SeqNum { get; set; }
        public string Sender { get; set; } = "";
        public string Target { get; set; } = "";
    }
}

/// <summary>
/// Raw FIX message received from network.
/// </summary>
public readonly struct RawFixMessage
{
    public byte[] RawBytes { get; init; }
    public long ReceiveTimestamp { get; init; }
}
