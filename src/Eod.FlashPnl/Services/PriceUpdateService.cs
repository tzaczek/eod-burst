namespace Eod.FlashPnl.Services;

/// <summary>
/// Simulates receiving price updates from market data feed.
/// In production, this would consume from a real market data source.
/// </summary>
public sealed class PriceUpdateService : BackgroundService
{
    private readonly PriceService _priceService;
    private readonly ILogger<PriceUpdateService> _logger;
    private readonly IConfiguration _config;

    private static readonly string[] Symbols =
    {
        "AAPL", "MSFT", "GOOGL", "AMZN", "META", "NVDA", "TSLA", "JPM",
        "V", "JNJ", "WMT", "PG", "MA", "UNH", "HD", "DIS", "BAC", "XOM",
        "COST", "ABBV", "PFE", "AVGO", "KO", "PEP", "TMO", "CSCO", "MRK"
    };

    private static readonly Dictionary<string, decimal> BasePrices = new()
    {
        ["AAPL"] = 175.50m,
        ["MSFT"] = 378.25m,
        ["GOOGL"] = 141.80m,
        ["AMZN"] = 178.50m,
        ["META"] = 505.75m,
        ["NVDA"] = 875.25m,
        ["TSLA"] = 248.50m,
        ["JPM"] = 195.75m,
        ["V"] = 280.25m,
        ["JNJ"] = 156.50m,
        ["WMT"] = 165.25m,
        ["PG"] = 158.75m,
        ["MA"] = 458.50m,
        ["UNH"] = 528.25m,
        ["HD"] = 378.50m,
        ["DIS"] = 112.75m,
        ["BAC"] = 35.50m,
        ["XOM"] = 105.25m,
        ["COST"] = 725.50m,
        ["ABBV"] = 178.25m,
        ["PFE"] = 28.75m,
        ["AVGO"] = 1285.50m,
        ["KO"] = 62.25m,
        ["PEP"] = 175.50m,
        ["TMO"] = 578.25m,
        ["CSCO"] = 52.75m,
        ["MRK"] = 128.50m
    };

    public PriceUpdateService(
        PriceService priceService,
        IConfiguration config,
        ILogger<PriceUpdateService> logger)
    {
        _priceService = priceService;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _config.GetValue("PriceSimulator:Enabled", true);
        if (!enabled)
        {
            _logger.LogInformation("Price simulator is disabled");
            return;
        }

        _logger.LogInformation("Price update service starting...");

        // Wait for dependencies
        await Task.Delay(3000, stoppingToken);

        var random = new Random();

        // Initialize with stale prices (yesterday's close)
        foreach (var symbol in Symbols)
        {
            var basePrice = BasePrices.GetValueOrDefault(symbol, 100m);
            var priceMantissa = (long)(basePrice * 100_000_000);
            await _priceService.SetPriceAsync(symbol, "stale", priceMantissa);
        }

        _logger.LogInformation("Initialized stale prices for {Count} symbols", Symbols.Length);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Update mid prices periodically (simulating live quotes)
                foreach (var symbol in Symbols)
                {
                    var basePrice = BasePrices.GetValueOrDefault(symbol, 100m);
                    
                    // Add small random variation (±0.5%)
                    var variation = (decimal)(random.NextDouble() - 0.5) * 0.01m;
                    var midPrice = basePrice * (1 + variation);
                    var priceMantissa = (long)(midPrice * 100_000_000);

                    await _priceService.SetMidPriceAsync(symbol, priceMantissa);
                }

                // Simulate EOD close prices at 4:00 PM
                var now = DateTime.Now;
                if (now.Hour == 16 && now.Minute == 0 && now.Second < 10)
                {
                    _logger.LogWarning("Publishing official close prices!");
                    
                    foreach (var symbol in Symbols)
                    {
                        var basePrice = BasePrices.GetValueOrDefault(symbol, 100m);
                        
                        // Official close is base price with small daily change
                        var dailyChange = (decimal)(random.NextDouble() - 0.5) * 0.04m; // ±2%
                        var closePrice = basePrice * (1 + dailyChange);
                        var priceMantissa = (long)(closePrice * 100_000_000);

                        await _priceService.SetOfficialCloseAsync(symbol, priceMantissa);
                    }
                }

                await Task.Delay(1000, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error updating prices");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }
}
