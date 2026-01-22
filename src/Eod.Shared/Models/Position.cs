namespace Eod.Shared.Models;

/// <summary>
/// Represents a trading position for a specific trader and symbol.
/// This class is designed for high-frequency updates with minimal allocations.
/// </summary>
public sealed class Position
{
    public string TraderId { get; }
    public string Symbol { get; }
    
    // Position state
    public long Quantity { get; private set; }
    public long TotalBuyQuantity { get; private set; }
    public long TotalSellQuantity { get; private set; }
    
    // Cost basis tracking (mantissa form, multiply by 10^-8 to get decimal)
    public long TotalBuyCostMantissa { get; private set; }
    public long TotalSellProceedsMantissa { get; private set; }
    
    // P&L tracking
    public long RealizedPnlMantissa { get; private set; }
    
    // Trade count for average price
    public int TradeCount { get; private set; }
    
    // Last update timestamp
    public long LastUpdateTicks { get; private set; }
    
    public Position(string traderId, string symbol)
    {
        TraderId = traderId;
        Symbol = symbol;
    }
    
    /// <summary>
    /// Applies a trade to this position. Thread-safe when called from a single partition consumer.
    /// </summary>
    public void ApplyTrade(long quantity, long priceMantissa, bool isBuy, long timestampTicks)
    {
        var costMantissa = quantity * priceMantissa;
        
        if (isBuy)
        {
            // Buying increases position
            Quantity += quantity;
            TotalBuyQuantity += quantity;
            TotalBuyCostMantissa += costMantissa;
        }
        else
        {
            // Selling decreases position and may realize P&L
            var previousQty = Quantity;
            Quantity -= quantity;
            TotalSellQuantity += quantity;
            TotalSellProceedsMantissa += costMantissa;
            
            // Calculate realized P&L if closing position
            if (previousQty > 0 && TotalBuyQuantity > 0)
            {
                // Average buy price
                var avgBuyPriceMantissa = TotalBuyCostMantissa / TotalBuyQuantity;
                // Realized = (Sell Price - Avg Buy Price) * Quantity Sold
                RealizedPnlMantissa += (priceMantissa - avgBuyPriceMantissa) * quantity;
            }
        }
        
        TradeCount++;
        LastUpdateTicks = timestampTicks;
    }
    
    /// <summary>
    /// Calculates unrealized P&L given a mark price.
    /// </summary>
    public long CalculateUnrealizedPnlMantissa(long markPriceMantissa)
    {
        if (Quantity == 0 || TotalBuyQuantity == 0)
            return 0;
            
        // Average cost basis
        var avgCostMantissa = TotalBuyCostMantissa / TotalBuyQuantity;
        
        // Unrealized = (Mark - Avg Cost) * Current Position
        return (markPriceMantissa - avgCostMantissa) * Quantity;
    }
    
    /// <summary>
    /// Gets the average entry price in mantissa form.
    /// </summary>
    public long GetAverageEntryPriceMantissa()
    {
        if (TotalBuyQuantity == 0)
            return 0;
        return TotalBuyCostMantissa / TotalBuyQuantity;
    }
    
    /// <summary>
    /// Creates a snapshot for serialization/caching.
    /// </summary>
    public PositionSnapshot ToSnapshot(long markPriceMantissa, string markSource)
    {
        return new PositionSnapshot
        {
            TraderId = TraderId,
            Symbol = Symbol,
            Quantity = Quantity,
            RealizedPnlMantissa = RealizedPnlMantissa,
            UnrealizedPnlMantissa = CalculateUnrealizedPnlMantissa(markPriceMantissa),
            MarkPriceMantissa = markPriceMantissa,
            MarkSource = markSource,
            TradeCount = TradeCount,
            LastUpdateTicks = LastUpdateTicks
        };
    }
}

/// <summary>
/// Immutable snapshot of position state for publishing.
/// </summary>
public readonly record struct PositionSnapshot
{
    public required string TraderId { get; init; }
    public required string Symbol { get; init; }
    public required long Quantity { get; init; }
    public required long RealizedPnlMantissa { get; init; }
    public required long UnrealizedPnlMantissa { get; init; }
    public required long MarkPriceMantissa { get; init; }
    public required string MarkSource { get; init; }
    public required int TradeCount { get; init; }
    public required long LastUpdateTicks { get; init; }
    
    public long TotalPnlMantissa => RealizedPnlMantissa + UnrealizedPnlMantissa;
}
