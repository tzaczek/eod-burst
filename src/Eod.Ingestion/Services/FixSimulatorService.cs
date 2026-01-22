using System.Diagnostics;
using System.Text;

namespace Eod.Ingestion.Services;

/// <summary>
/// Simulates FIX message generation for testing.
/// Generates realistic EOD burst patterns.
/// </summary>
public sealed class FixSimulatorService : BackgroundService
{
    private readonly MessageChannel _messageChannel;
    private readonly ILogger<FixSimulatorService> _logger;
    private readonly IConfiguration _config;
    
    private static readonly string[] Symbols = 
    {
        "AAPL", "MSFT", "GOOGL", "AMZN", "META", "NVDA", "TSLA", "JPM", 
        "V", "JNJ", "WMT", "PG", "MA", "UNH", "HD", "DIS", "BAC", "XOM",
        "COST", "ABBV", "PFE", "AVGO", "KO", "PEP", "TMO", "CSCO", "MRK"
    };
    
    private static readonly string[] TraderIds = 
    {
        "T001", "T002", "T003", "T004", "T005", "T006", "T007", "T008"
    };
    
    private static readonly string[] Strategies =
    {
        "VWAP", "TWAP", "MOC", "LOC", "IMPL", "PAIRS"
    };

    public FixSimulatorService(
        MessageChannel messageChannel,
        IConfiguration config,
        ILogger<FixSimulatorService> logger)
    {
        _messageChannel = messageChannel;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _config.GetValue("Simulator:Enabled", false);
        if (!enabled)
        {
            _logger.LogInformation("FIX Simulator is disabled");
            return;
        }

        var baseRate = _config.GetValue("Simulator:BaseRatePerSecond", 100);
        var burstMultiplier = _config.GetValue("Simulator:BurstMultiplier", 10);
        var burstDurationSeconds = _config.GetValue("Simulator:BurstDurationSeconds", 60);
        
        _logger.LogInformation(
            "FIX Simulator starting. Base rate: {Rate}/sec, Burst: {Mult}x for {Duration}s",
            baseRate, burstMultiplier, burstDurationSeconds);

        // Wait for ingestion service to be ready
        await Task.Delay(5000, stoppingToken);
        
        var random = new Random();
        var seqNum = 1;
        var totalGenerated = 0L;
        var burstMode = false;
        var burstStartTime = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            // Check for burst mode trigger (simulating EOD)
            var now = DateTime.Now;
            var isEodTime = now.Hour == 16 && now.Minute < 5; // 4:00-4:05 PM
            var simulateBurst = _config.GetValue("Simulator:SimulateBurstNow", false);
            
            if ((isEodTime || simulateBurst) && !burstMode)
            {
                burstMode = true;
                burstStartTime = DateTime.UtcNow;
                _logger.LogWarning("🚀 BURST MODE ACTIVATED - {Mult}x volume!", burstMultiplier);
            }
            
            if (burstMode && (DateTime.UtcNow - burstStartTime).TotalSeconds > burstDurationSeconds)
            {
                burstMode = false;
                _logger.LogInformation("Burst mode ended, returning to normal rate");
            }

            var currentRate = burstMode ? baseRate * burstMultiplier : baseRate;
            var delayMs = 1000.0 / currentRate;

            // Generate message
            var symbol = Symbols[random.Next(Symbols.Length)];
            var traderId = TraderIds[random.Next(TraderIds.Length)];
            var strategy = Strategies[random.Next(Strategies.Length)];
            var side = random.Next(2) == 0 ? "1" : "2"; // Buy or Sell
            var quantity = (random.Next(1, 100) * 100).ToString();
            var price = (100 + random.NextDouble() * 200).ToString("F2");
            var execId = $"E{DateTime.UtcNow:yyyyMMddHHmmssfff}{seqNum:D6}";
            var orderId = $"O{DateTime.UtcNow:yyyyMMdd}{random.Next(100000, 999999)}";
            
            var fixMessage = BuildFixMessage(
                seqNum++,
                execId,
                orderId,
                symbol,
                side,
                quantity,
                price,
                traderId,
                strategy);

            var rawMessage = new RawFixMessage
            {
                RawBytes = Encoding.ASCII.GetBytes(fixMessage),
                ReceiveTimestamp = Stopwatch.GetTimestamp()
            };

            await _messageChannel.EnqueueAsync(rawMessage, stoppingToken);
            totalGenerated++;

            if (totalGenerated % 10000 == 0)
            {
                _logger.LogInformation(
                    "Generated {Count} messages. Mode: {Mode}, Rate: {Rate}/sec",
                    totalGenerated,
                    burstMode ? "BURST" : "normal",
                    currentRate);
            }

            // Throttle to target rate
            await Task.Delay(TimeSpan.FromMilliseconds(delayMs), stoppingToken);
        }

        _logger.LogInformation("FIX Simulator stopped. Total generated: {Count}", totalGenerated);
    }

    private static string BuildFixMessage(
        int seqNum,
        string execId,
        string orderId,
        string symbol,
        string side,
        string quantity,
        string price,
        string traderId,
        string strategy)
    {
        var sb = new StringBuilder();
        
        // Build body first to calculate length
        var body = new StringBuilder();
        body.Append($"35=8\x01");           // MsgType = ExecutionReport
        body.Append($"49=EXCHANGE\x01");    // SenderCompID
        body.Append($"56=CLIENT\x01");      // TargetCompID
        body.Append($"34={seqNum}\x01");    // MsgSeqNum
        body.Append($"52={DateTime.UtcNow:yyyyMMdd-HH:mm:ss.fff}\x01"); // SendingTime
        body.Append($"17={execId}\x01");    // ExecID
        body.Append($"37={orderId}\x01");   // OrderID
        body.Append($"11=CL{orderId}\x01"); // ClOrdID
        body.Append($"55={symbol}\x01");    // Symbol
        body.Append($"54={side}\x01");      // Side
        body.Append($"38={quantity}\x01");  // OrderQty
        body.Append($"44={price}\x01");     // Price
        body.Append($"32={quantity}\x01");  // LastQty
        body.Append($"31={price}\x01");     // LastPx
        body.Append($"14={quantity}\x01");  // CumQty
        body.Append($"6={price}\x01");      // AvgPx
        body.Append($"39=2\x01");           // OrdStatus = Filled
        body.Append($"150=F\x01");          // ExecType = Fill
        body.Append($"1=ACCT001\x01");      // Account
        body.Append($"207=NYSE\x01");       // SecurityExchange
        body.Append($"5001={traderId}\x01"); // Custom: TraderId
        body.Append($"5002={strategy}\x01"); // Custom: Strategy

        var bodyStr = body.ToString();
        
        // Header
        sb.Append($"8=FIX.4.2\x01");
        sb.Append($"9={bodyStr.Length}\x01");
        sb.Append(bodyStr);

        // Calculate checksum
        var fullMsg = sb.ToString();
        var checksum = 0;
        foreach (var c in fullMsg)
        {
            checksum += c;
        }
        checksum %= 256;

        sb.Append($"10={checksum:D3}\x01");

        return sb.ToString();
    }
}
