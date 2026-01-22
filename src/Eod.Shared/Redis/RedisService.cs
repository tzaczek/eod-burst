using System.Text.Json;
using Eod.Shared.Configuration;
using Eod.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Eod.Shared.Redis;

/// <summary>
/// Redis service for position state storage and P&L publishing.
/// </summary>
public sealed class RedisService : IRedisService, IDisposable
{
    private readonly ConnectionMultiplexer _connection;
    private readonly IDatabase _db;
    private readonly ISubscriber _subscriber;
    private readonly ILogger<RedisService> _logger;
    private readonly RedisSettings _settings;
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public RedisService(
        IOptions<RedisSettings> settings,
        ILogger<RedisService> logger)
    {
        _settings = settings.Value;
        _logger = logger;

        var config = ConfigurationOptions.Parse(_settings.ConnectionString);
        config.ConnectTimeout = _settings.ConnectTimeoutMs;
        config.SyncTimeout = _settings.SyncTimeoutMs;
        config.ConnectRetry = _settings.ConnectRetry;
        config.AbortOnConnectFail = false;

        _connection = ConnectionMultiplexer.Connect(config);
        _db = _connection.GetDatabase();
        _subscriber = _connection.GetSubscriber();

        _connection.ConnectionFailed += (_, e) =>
            _logger.LogWarning("Redis connection failed: {Reason}", e.Exception?.Message);
        
        _connection.ConnectionRestored += (_, e) =>
            _logger.LogInformation("Redis connection restored");

        _logger.LogInformation("Redis connected to {Endpoint}", _settings.ConnectionString);
    }

    /// <summary>
    /// Updates position in Redis hash.
    /// </summary>
    public async Task UpdatePositionAsync(PositionSnapshot snapshot)
    {
        var key = $"{_settings.PositionKeyPrefix}:{snapshot.TraderId}";
        
        var entries = new HashEntry[]
        {
            new(snapshot.Symbol, snapshot.Quantity),
            new($"{snapshot.Symbol}:pnl", snapshot.TotalPnlMantissa),
            new($"{snapshot.Symbol}:mark", snapshot.MarkPriceMantissa),
            new($"{snapshot.Symbol}:source", snapshot.MarkSource),
            new($"{snapshot.Symbol}:trades", snapshot.TradeCount)
        };

        await _db.HashSetAsync(key, entries);
    }

    /// <summary>
    /// Publishes P&L update to subscribers.
    /// </summary>
    public async Task PublishPnlUpdateAsync(PositionSnapshot snapshot)
    {
        var channel = $"{_settings.PnlUpdateChannel}:{snapshot.TraderId}";
        var message = JsonSerializer.Serialize(snapshot, JsonOptions);
        
        await _subscriber.PublishAsync(RedisChannel.Literal(channel), message);
    }

    /// <summary>
    /// Updates position and publishes in a single operation.
    /// </summary>
    public async Task UpdateAndPublishAsync(PositionSnapshot snapshot)
    {
        await UpdatePositionAsync(snapshot);
        await PublishPnlUpdateAsync(snapshot);
    }

    /// <summary>
    /// Gets position for a trader and symbol.
    /// </summary>
    public async Task<long?> GetPositionAsync(string traderId, string symbol)
    {
        var key = $"{_settings.PositionKeyPrefix}:{traderId}";
        var value = await _db.HashGetAsync(key, symbol);
        
        return value.HasValue ? (long)value : null;
    }

    /// <summary>
    /// Gets all positions for a trader.
    /// </summary>
    public async Task<Dictionary<string, long>> GetAllPositionsAsync(string traderId)
    {
        var key = $"{_settings.PositionKeyPrefix}:{traderId}";
        var entries = await _db.HashGetAllAsync(key);
        
        return entries
            .Where(e => !e.Name.ToString().Contains(':'))
            .ToDictionary(
                e => e.Name.ToString(),
                e => (long)e.Value);
    }

    /// <summary>
    /// Sets a price in the cache.
    /// </summary>
    public async Task SetPriceAsync(string symbol, string priceType, long priceMantissa)
    {
        var key = $"{_settings.PriceKeyPrefix}:{priceType}:{symbol}";
        await _db.StringSetAsync(key, priceMantissa, TimeSpan.FromHours(24));
    }

    /// <summary>
    /// Gets a price from the cache.
    /// </summary>
    public async Task<long?> GetPriceAsync(string symbol, string priceType)
    {
        var key = $"{_settings.PriceKeyPrefix}:{priceType}:{symbol}";
        var value = await _db.StringGetAsync(key);
        
        return value.HasValue ? (long)value : null;
    }

    /// <summary>
    /// Gets price using waterfall logic (official → ltp → mid → stale).
    /// </summary>
    public async Task<(long PriceMantissa, string Source)> GetMarkPriceAsync(string symbol)
    {
        // Try official close first
        var official = await GetPriceAsync(symbol, "close");
        if (official.HasValue)
            return (official.Value, "OFFICIAL");

        // Try last traded price
        var ltp = await GetPriceAsync(symbol, "ltp");
        if (ltp.HasValue)
            return (ltp.Value, "LTP");

        // Try mid price
        var mid = await GetPriceAsync(symbol, "mid");
        if (mid.HasValue)
            return (mid.Value, "MID");

        // Fall back to stale/yesterday's close
        var stale = await GetPriceAsync(symbol, "stale");
        return (stale ?? 0, "STALE");
    }

    /// <summary>
    /// Subscribes to P&L updates for a trader.
    /// </summary>
    public async Task SubscribeToPnlUpdatesAsync(
        string traderId,
        Action<PositionSnapshot> handler)
    {
        var channel = $"{_settings.PnlUpdateChannel}:{traderId}";
        
        await _subscriber.SubscribeAsync(RedisChannel.Literal(channel), (_, message) =>
        {
            if (message.HasValue)
            {
                var snapshot = JsonSerializer.Deserialize<PositionSnapshot>(message!, JsonOptions);
                handler(snapshot);
            }
        });
    }

    /// <summary>
    /// Checks if Redis is connected and responsive.
    /// </summary>
    public async Task<bool> PingAsync()
    {
        try
        {
            var latency = await _db.PingAsync();
            return latency < TimeSpan.FromSeconds(1);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
