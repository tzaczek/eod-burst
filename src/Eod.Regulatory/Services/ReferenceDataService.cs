using System.Collections.Concurrent;

namespace Eod.Regulatory.Services;

/// <summary>
/// Service for looking up reference data (traders, strategies, securities).
/// In production, this would connect to a master data system.
/// </summary>
public sealed class ReferenceDataService
{
    private readonly ILogger<ReferenceDataService> _logger;
    
    // Simulated reference data caches
    private readonly ConcurrentDictionary<string, TraderInfo> _traders = new();
    private readonly ConcurrentDictionary<string, StrategyInfo> _strategies = new();
    private readonly ConcurrentDictionary<string, SecurityInfo> _securities = new();
    
    private bool _initialized;

    public ReferenceDataService(ILogger<ReferenceDataService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Initializes reference data. Call on startup.
    /// </summary>
    public Task InitializeAsync()
    {
        if (_initialized)
            return Task.CompletedTask;

        // Simulate loading reference data
        InitializeTraders();
        InitializeStrategies();
        InitializeSecurities();

        _initialized = true;
        _logger.LogInformation(
            "Reference data initialized: {Traders} traders, {Strategies} strategies, {Securities} securities",
            _traders.Count,
            _strategies.Count,
            _securities.Count);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets trader information.
    /// </summary>
    public Task<TraderInfo?> GetTraderAsync(string traderId)
    {
        _traders.TryGetValue(traderId, out var trader);
        return Task.FromResult(trader);
    }

    /// <summary>
    /// Gets strategy information.
    /// </summary>
    public Task<StrategyInfo?> GetStrategyAsync(string strategyCode)
    {
        _strategies.TryGetValue(strategyCode, out var strategy);
        return Task.FromResult(strategy);
    }

    /// <summary>
    /// Gets security information.
    /// </summary>
    public Task<SecurityInfo?> GetSecurityAsync(string symbol)
    {
        _securities.TryGetValue(symbol, out var security);
        return Task.FromResult(security);
    }

    private void InitializeTraders()
    {
        var traders = new[]
        {
            new TraderInfo("T001", "John Smith", "MPID001", "CRD001001"),
            new TraderInfo("T002", "Jane Doe", "MPID001", "CRD001002"),
            new TraderInfo("T003", "Bob Wilson", "MPID001", "CRD001003"),
            new TraderInfo("T004", "Alice Brown", "MPID002", "CRD002001"),
            new TraderInfo("T005", "Charlie Davis", "MPID002", "CRD002002"),
            new TraderInfo("T006", "Diana Lee", "MPID002", "CRD002003"),
            new TraderInfo("T007", "Edward Kim", "MPID003", "CRD003001"),
            new TraderInfo("T008", "Fiona Chen", "MPID003", "CRD003002")
        };

        foreach (var trader in traders)
        {
            _traders[trader.TraderId] = trader;
        }
    }

    private void InitializeStrategies()
    {
        var strategies = new[]
        {
            new StrategyInfo("VWAP", "Volume Weighted Average Price", "ALGO"),
            new StrategyInfo("TWAP", "Time Weighted Average Price", "ALGO"),
            new StrategyInfo("MOC", "Market On Close", "CLOSING"),
            new StrategyInfo("LOC", "Limit On Close", "CLOSING"),
            new StrategyInfo("IMPL", "Implementation Shortfall", "ALGO"),
            new StrategyInfo("PAIRS", "Pairs Trading", "QUANT")
        };

        foreach (var strategy in strategies)
        {
            _strategies[strategy.Code] = strategy;
        }
    }

    private void InitializeSecurities()
    {
        var securities = new[]
        {
            new SecurityInfo("AAPL", "Apple Inc.", "037833100", "2046251", "US0378331005"),
            new SecurityInfo("MSFT", "Microsoft Corporation", "594918104", "2588173", "US5949181045"),
            new SecurityInfo("GOOGL", "Alphabet Inc.", "02079K305", "BYVY8G0", "US02079K3059"),
            new SecurityInfo("AMZN", "Amazon.com Inc.", "023135106", "2000019", "US0231351067"),
            new SecurityInfo("META", "Meta Platforms Inc.", "30303M102", "B7TL820", "US30303M1027"),
            new SecurityInfo("NVDA", "NVIDIA Corporation", "67066G104", "2379504", "US67066G1040"),
            new SecurityInfo("TSLA", "Tesla Inc.", "88160R101", "B616C79", "US88160R1014"),
            new SecurityInfo("JPM", "JPMorgan Chase & Co.", "46625H100", "2190385", "US46625H1005"),
            new SecurityInfo("V", "Visa Inc.", "92826C839", "B2PZN04", "US92826C8394"),
            new SecurityInfo("JNJ", "Johnson & Johnson", "478160104", "2475833", "US4781601046"),
            new SecurityInfo("WMT", "Walmart Inc.", "931142103", "2936921", "US9311421039"),
            new SecurityInfo("PG", "Procter & Gamble Company", "742718109", "2704407", "US7427181091"),
            new SecurityInfo("MA", "Mastercard Inc.", "57636Q104", "B121557", "US57636Q1040"),
            new SecurityInfo("UNH", "UnitedHealth Group Inc.", "91324P102", "2917766", "US91324P1021"),
            new SecurityInfo("HD", "Home Depot Inc.", "437076102", "2434209", "US4370761029"),
            new SecurityInfo("DIS", "Walt Disney Company", "254687106", "2270726", "US2546871060"),
            new SecurityInfo("BAC", "Bank of America Corporation", "060505104", "2295677", "US0605051046"),
            new SecurityInfo("XOM", "Exxon Mobil Corporation", "30231G102", "2326618", "US30231G1022"),
            new SecurityInfo("COST", "Costco Wholesale Corporation", "22160K105", "2701271", "US22160K1051"),
            new SecurityInfo("ABBV", "AbbVie Inc.", "00287Y109", "B92SR70", "US00287Y1091"),
            new SecurityInfo("PFE", "Pfizer Inc.", "717081103", "2684703", "US7170811035"),
            new SecurityInfo("AVGO", "Broadcom Inc.", "11135F101", "BDZ78H9", "US11135F1012"),
            new SecurityInfo("KO", "Coca-Cola Company", "191216100", "2206657", "US1912161007"),
            new SecurityInfo("PEP", "PepsiCo Inc.", "713448108", "2681511", "US7134481081"),
            new SecurityInfo("TMO", "Thermo Fisher Scientific Inc.", "883556102", "2886907", "US8835561023"),
            new SecurityInfo("CSCO", "Cisco Systems Inc.", "17275R102", "2198163", "US17275R1023"),
            new SecurityInfo("MRK", "Merck & Co. Inc.", "58933Y105", "2778844", "US58933Y1055")
        };

        foreach (var security in securities)
        {
            _securities[security.Symbol] = security;
        }
    }
}

public record TraderInfo(
    string TraderId,
    string Name,
    string Mpid,
    string Crd);

public record StrategyInfo(
    string Code,
    string Name,
    string Type);

public record SecurityInfo(
    string Symbol,
    string Name,
    string Cusip,
    string Sedol,
    string Isin);
