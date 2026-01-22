using Eod.Shared.Models;
using Eod.Shared.Redis;
using Eod.Shared.Resilience;

namespace Eod.FlashPnl.Services;

/// <summary>
/// Decorator that wraps IRedisService with Circuit Breaker protection.
/// Follows Decorator Pattern - adds resilience without modifying original service.
/// 
/// WHY THIS EXISTS:
/// During EOD burst, Redis can become overwhelmed with 50K+ pub/sub messages.
/// Without circuit breaker:
///   - Each failed Redis call waits for timeout (3-5 seconds)
///   - Kafka consumer backs up (trade processing blocks on Redis)
///   - Consumer group rebalances (Kafka thinks consumer is dead)
///   - Complete system failure
/// 
/// With circuit breaker:
///   - After 5 failures, circuit OPENS
///   - Redis calls fail immediately (no timeout wait)
///   - Trade processing continues (positions updated in memory)
///   - P&L publishing resumes automatically when Redis recovers
/// </summary>
public sealed class ResilientRedisService
{
    private readonly IRedisService _redis;
    private readonly ICircuitBreaker _publishCircuitBreaker;
    private readonly ICircuitBreaker _queryCircuitBreaker;
    private readonly ILogger<ResilientRedisService> _logger;
    
    // Track failures for monitoring
    private long _publishFailures;
    private long _queryFailures;
    private long _circuitBreakerRejections;

    public ResilientRedisService(
        IRedisService redis,
        ICircuitBreakerFactory circuitBreakerFactory,
        ILogger<ResilientRedisService> logger)
    {
        _redis = redis;
        _logger = logger;

        // Circuit breaker for publish operations (P&L updates)
        // More aggressive - publishers can afford to skip updates during outage
        _publishCircuitBreaker = circuitBreakerFactory.GetOrCreate(
            "FlashPnl-Redis-Publish",
            new CircuitBreakerOptions
            {
                Name = "FlashPnl-Redis-Publish",
                FailureThreshold = 5,           // Open after 5 failures
                OpenDuration = TimeSpan.FromSeconds(15),  // Short - P&L is time-sensitive
                SuccessThresholdInHalfOpen = 2, // Need 2 successes to close
                FailureWindow = TimeSpan.FromSeconds(30),
                ExceptionTypes = [typeof(RedisException), typeof(TimeoutException)]
            });

        // Circuit breaker for query operations (price lookups)
        // Less aggressive - we have local cache as fallback
        _queryCircuitBreaker = circuitBreakerFactory.GetOrCreate(
            "FlashPnl-Redis-Query",
            new CircuitBreakerOptions
            {
                Name = "FlashPnl-Redis-Query",
                FailureThreshold = 10,          // More tolerant
                OpenDuration = TimeSpan.FromSeconds(10), // Quick recovery check
                SuccessThresholdInHalfOpen = 1,
                FailureWindow = TimeSpan.FromSeconds(60),
                ExceptionTypes = [typeof(RedisException), typeof(TimeoutException)]
            });

        // Log circuit state changes for monitoring
        _publishCircuitBreaker.StateChanged += OnCircuitStateChanged;
        _queryCircuitBreaker.StateChanged += OnCircuitStateChanged;
    }

    /// <summary>
    /// Updates position and publishes P&L update with circuit breaker protection.
    /// If circuit is open, silently skips (trade processing is more important than publishing).
    /// </summary>
    public async Task<bool> UpdateAndPublishAsync(PositionSnapshot snapshot)
    {
        if (_publishCircuitBreaker.State == CircuitBreakerState.Open)
        {
            Interlocked.Increment(ref _circuitBreakerRejections);
            _logger.LogDebug(
                "Redis publish circuit open - skipping P&L update for {TraderId}:{Symbol}",
                snapshot.TraderId, snapshot.Symbol);
            return false;
        }

        try
        {
            await _publishCircuitBreaker.ExecuteAsync(async ct =>
            {
                await _redis.UpdateAndPublishAsync(snapshot);
            });
            return true;
        }
        catch (CircuitBreakerOpenException)
        {
            Interlocked.Increment(ref _circuitBreakerRejections);
            return false;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _publishFailures);
            _logger.LogWarning(ex, 
                "Redis publish failed for {TraderId}:{Symbol}", 
                snapshot.TraderId, snapshot.Symbol);
            return false;
        }
    }

    /// <summary>
    /// Gets mark price with circuit breaker protection.
    /// Returns null if circuit is open (caller should use local cache).
    /// </summary>
    public async Task<(long PriceMantissa, string Source)?> GetMarkPriceAsync(string symbol)
    {
        if (_queryCircuitBreaker.State == CircuitBreakerState.Open)
        {
            return null; // Caller uses local cache
        }

        try
        {
            return await _queryCircuitBreaker.ExecuteAsync(async ct =>
            {
                return await _redis.GetMarkPriceAsync(symbol);
            });
        }
        catch (CircuitBreakerOpenException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _queryFailures);
            _logger.LogDebug(ex, "Redis price query failed for {Symbol}", symbol);
            return null;
        }
    }

    /// <summary>
    /// Sets price with circuit breaker protection.
    /// Fire-and-forget semantics - failures logged but not propagated.
    /// </summary>
    public async Task SetPriceAsync(string symbol, string priceType, long priceMantissa)
    {
        if (_publishCircuitBreaker.State == CircuitBreakerState.Open)
        {
            _logger.LogDebug("Redis price set circuit open - skipping {Symbol}:{Type}", symbol, priceType);
            return;
        }

        try
        {
            await _publishCircuitBreaker.ExecuteAsync(async ct =>
            {
                await _redis.SetPriceAsync(symbol, priceType, priceMantissa);
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Redis price set failed for {Symbol}:{Type}", symbol, priceType);
        }
    }

    /// <summary>
    /// Checks Redis connectivity (bypasses circuit breaker - used for health checks).
    /// </summary>
    public Task<bool> PingAsync() => _redis.PingAsync();

    /// <summary>
    /// Gets position (bypasses circuit breaker - individual queries are fine).
    /// </summary>
    public Task<long?> GetPositionAsync(string traderId, string symbol) =>
        _redis.GetPositionAsync(traderId, symbol);

    // Metrics for monitoring
    public CircuitBreakerState PublishCircuitState => _publishCircuitBreaker.State;
    public CircuitBreakerState QueryCircuitState => _queryCircuitBreaker.State;
    public CircuitBreakerMetrics PublishMetrics => _publishCircuitBreaker.Metrics;
    public CircuitBreakerMetrics QueryMetrics => _queryCircuitBreaker.Metrics;
    public long PublishFailures => Interlocked.Read(ref _publishFailures);
    public long QueryFailures => Interlocked.Read(ref _queryFailures);
    public long CircuitBreakerRejections => Interlocked.Read(ref _circuitBreakerRejections);

    private void OnCircuitStateChanged(object? sender, CircuitBreakerStateChangedEventArgs e)
    {
        var logLevel = e.NewState == CircuitBreakerState.Open ? LogLevel.Warning : LogLevel.Information;
        
        _logger.Log(logLevel,
            "Circuit breaker '{Name}' state changed: {PreviousState} → {NewState}",
            e.CircuitBreakerName, e.PreviousState, e.NewState);
    }
}

/// <summary>
/// Redis-specific exception for circuit breaker filtering.
/// </summary>
public class RedisException : Exception
{
    public RedisException(string message) : base(message) { }
    public RedisException(string message, Exception inner) : base(message, inner) { }
}
