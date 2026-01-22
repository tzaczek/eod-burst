using System.Collections.Concurrent;
using Eod.Shared.Models;

namespace Eod.FlashPnl.Services;

/// <summary>
/// Thread-safe in-memory position store optimized for high-frequency updates.
/// </summary>
public sealed class PositionStore
{
    private readonly ConcurrentDictionary<PositionKey, Position> _positions = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _traderSymbols = new();
    
    private long _updateCount;

    /// <summary>
    /// Gets or creates a position for the given trader and symbol.
    /// </summary>
    public Position GetOrCreate(string traderId, string symbol)
    {
        var key = new PositionKey(traderId, symbol);
        return _positions.GetOrAdd(key, k => 
        {
            // Track which symbols each trader has
            _traderSymbols.AddOrUpdate(
                traderId,
                _ => [symbol],
                (_, symbols) => { symbols.Add(symbol); return symbols; });
            
            return new Position(k.TraderId, k.Symbol);
        });
    }

    /// <summary>
    /// Applies a trade to the appropriate position and returns the updated position.
    /// </summary>
    public Position ApplyTrade(
        string traderId, 
        string symbol, 
        long quantity, 
        long priceMantissa, 
        bool isBuy,
        long timestampTicks)
    {
        var position = GetOrCreate(traderId, symbol);
        position.ApplyTrade(quantity, priceMantissa, isBuy, timestampTicks);
        Interlocked.Increment(ref _updateCount);
        return position;
    }

    /// <summary>
    /// Gets a position if it exists.
    /// </summary>
    public Position? Get(string traderId, string symbol)
    {
        var key = new PositionKey(traderId, symbol);
        return _positions.TryGetValue(key, out var position) ? position : null;
    }

    /// <summary>
    /// Gets all positions for a trader.
    /// </summary>
    public IEnumerable<Position> GetTraderPositions(string traderId)
    {
        if (!_traderSymbols.TryGetValue(traderId, out var symbols))
            yield break;

        foreach (var symbol in symbols.ToArray())
        {
            var key = new PositionKey(traderId, symbol);
            if (_positions.TryGetValue(key, out var position))
                yield return position;
        }
    }

    /// <summary>
    /// Gets all positions.
    /// </summary>
    public IEnumerable<Position> GetAllPositions() => _positions.Values;

    /// <summary>
    /// Gets total number of unique positions.
    /// </summary>
    public int GetPositionCount() => _positions.Count;

    /// <summary>
    /// Gets total number of unique traders.
    /// </summary>
    public int GetTraderCount() => _traderSymbols.Count;

    /// <summary>
    /// Gets total update count for metrics.
    /// </summary>
    public long GetUpdateCount() => Interlocked.Read(ref _updateCount);
}
