using Eod.Shared.Models;

namespace Eod.Shared.Redis;

/// <summary>
/// Interface for Redis operations.
/// Supports dependency injection and testability.
/// </summary>
public interface IRedisService
{
    /// <summary>
    /// Updates position in Redis hash.
    /// </summary>
    Task UpdatePositionAsync(PositionSnapshot snapshot);
    
    /// <summary>
    /// Publishes P&L update to subscribers.
    /// </summary>
    Task PublishPnlUpdateAsync(PositionSnapshot snapshot);
    
    /// <summary>
    /// Updates position and publishes in a single operation.
    /// </summary>
    Task UpdateAndPublishAsync(PositionSnapshot snapshot);
    
    /// <summary>
    /// Gets position for a trader and symbol.
    /// </summary>
    Task<long?> GetPositionAsync(string traderId, string symbol);
    
    /// <summary>
    /// Gets all positions for a trader.
    /// </summary>
    Task<Dictionary<string, long>> GetAllPositionsAsync(string traderId);
    
    /// <summary>
    /// Sets a price in the cache.
    /// </summary>
    Task SetPriceAsync(string symbol, string priceType, long priceMantissa);
    
    /// <summary>
    /// Gets a price from the cache.
    /// </summary>
    Task<long?> GetPriceAsync(string symbol, string priceType);
    
    /// <summary>
    /// Gets price using waterfall logic (official → ltp → mid → stale).
    /// </summary>
    Task<(long PriceMantissa, string Source)> GetMarkPriceAsync(string symbol);
    
    /// <summary>
    /// Subscribes to P&L updates for a trader.
    /// </summary>
    Task SubscribeToPnlUpdatesAsync(string traderId, Action<PositionSnapshot> handler);
    
    /// <summary>
    /// Checks if Redis is connected and responsive.
    /// </summary>
    Task<bool> PingAsync();
}
