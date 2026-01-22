using System.Collections.Concurrent;
using Eod.Shared.Resilience;

namespace Eod.FlashPnl.Services;

/// <summary>
/// Price service with waterfall marking logic and circuit breaker protection.
/// Maintains local cache backed by Redis with graceful degradation.
/// 
/// WHY CIRCUIT BREAKER HERE:
/// Price lookups happen on EVERY trade (50K+ per second during burst).
/// If Redis is slow/down:
///   - Without CB: Each lookup waits → trade processing bottleneck
///   - With CB: Returns from local cache instantly → maintains throughput
/// 
/// The local cache provides a natural fallback, making this service
/// resilient even when Redis is completely unavailable.
/// </summary>
public sealed class PriceService
{
    private readonly ResilientRedisService _redis;
    private readonly ILogger<PriceService> _logger;
    
    // Local cache for fast access (avoid Redis round-trip on every trade)
    private readonly ConcurrentDictionary<string, PriceEntry> _localCache = new();
    
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromSeconds(5);
    
    // Track degraded mode operations
    private long _cacheHits;
    private long _cacheMisses;
    private long _redisFallbacks;

    public PriceService(ResilientRedisService redis, ILogger<PriceService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    /// <summary>
    /// Gets the mark price for a symbol using waterfall logic.
    /// Gracefully degrades to cached data if Redis is unavailable.
    /// </summary>
    public async Task<(long PriceMantissa, string Source)> GetMarkAsync(string symbol)
    {
        // Check local cache first (fastest path)
        if (_localCache.TryGetValue(symbol, out var cached) && 
            DateTime.UtcNow - cached.CachedAt < CacheExpiry)
        {
            Interlocked.Increment(ref _cacheHits);
            return (cached.PriceMantissa, cached.Source);
        }

        Interlocked.Increment(ref _cacheMisses);

        // Try to fetch from Redis (circuit breaker protected)
        var redisResult = await _redis.GetMarkPriceAsync(symbol);
        
        if (redisResult.HasValue)
        {
            var (price, source) = redisResult.Value;
            
            // Update local cache
            _localCache[symbol] = new PriceEntry
            {
                PriceMantissa = price,
                Source = source,
                CachedAt = DateTime.UtcNow
            };

            return (price, source);
        }

        // Redis unavailable - use stale cache or default
        Interlocked.Increment(ref _redisFallbacks);
        
        if (_localCache.TryGetValue(symbol, out var staleCache))
        {
            _logger.LogDebug(
                "Using stale cache for {Symbol} (Redis unavailable)", symbol);
            return (staleCache.PriceMantissa, $"{staleCache.Source}-STALE");
        }

        // No cache at all - return unknown
        return (0, "UNKNOWN");
    }

    /// <summary>
    /// Gets mark price synchronously from local cache only.
    /// Returns stale data if not in cache (fast path for high-frequency access).
    /// This method NEVER blocks on I/O.
    /// </summary>
    public (long PriceMantissa, string Source) GetMarkFast(string symbol)
    {
        if (_localCache.TryGetValue(symbol, out var cached))
        {
            Interlocked.Increment(ref _cacheHits);
            return (cached.PriceMantissa, cached.Source);
        }

        Interlocked.Increment(ref _cacheMisses);
        
        // Not in cache - return zero with UNKNOWN source
        // Background refresh will populate it
        return (0, "UNKNOWN");
    }

    /// <summary>
    /// Updates a price in both local cache and Redis.
    /// Redis update is fire-and-forget with circuit breaker protection.
    /// </summary>
    public async Task SetPriceAsync(string symbol, string priceType, long priceMantissa)
    {
        // Always update local cache (fast, never fails)
        var source = priceType.ToUpperInvariant() switch
        {
            "CLOSE" => "OFFICIAL",
            "LTP" => "LTP",
            "MID" => "MID",
            _ => "STALE"
        };
        
        _localCache[symbol] = new PriceEntry
        {
            PriceMantissa = priceMantissa,
            Source = source,
            CachedAt = DateTime.UtcNow
        };

        // Fire-and-forget Redis update (circuit breaker protected)
        await _redis.SetPriceAsync(symbol, priceType, priceMantissa);
        
        _logger.LogDebug(
            "Price updated: {Symbol} {Type} = {Price}",
            symbol, priceType, priceMantissa / 100_000_000.0m);
    }

    /// <summary>
    /// Sets the official close price (highest priority).
    /// </summary>
    public Task SetOfficialCloseAsync(string symbol, long priceMantissa) =>
        SetPriceAsync(symbol, "close", priceMantissa);

    /// <summary>
    /// Sets the last traded price (second priority).
    /// </summary>
    public Task SetLastTradePriceAsync(string symbol, long priceMantissa) =>
        SetPriceAsync(symbol, "ltp", priceMantissa);

    /// <summary>
    /// Sets the bid/ask mid price (third priority).
    /// </summary>
    public Task SetMidPriceAsync(string symbol, long priceMantissa) =>
        SetPriceAsync(symbol, "mid", priceMantissa);

    /// <summary>
    /// Clears local cache. Useful after reconnect.
    /// </summary>
    public void ClearCache()
    {
        _localCache.Clear();
        _logger.LogInformation("Price cache cleared");
    }

    /// <summary>
    /// Gets current Redis circuit breaker state for monitoring.
    /// </summary>
    public CircuitBreakerState RedisCircuitState => _redis.QueryCircuitState;
    
    // Metrics for monitoring
    public long CacheHits => Interlocked.Read(ref _cacheHits);
    public long CacheMisses => Interlocked.Read(ref _cacheMisses);
    public long RedisFallbacks => Interlocked.Read(ref _redisFallbacks);

    private readonly struct PriceEntry
    {
        public long PriceMantissa { get; init; }
        public string Source { get; init; }
        public DateTime CachedAt { get; init; }
    }
}
