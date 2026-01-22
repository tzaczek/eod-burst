namespace Eod.Shared.Models;

/// <summary>
/// Composite key for position lookup. Implemented as a readonly struct for zero allocation.
/// </summary>
public readonly struct PositionKey : IEquatable<PositionKey>
{
    public string TraderId { get; }
    public string Symbol { get; }
    
    public PositionKey(string traderId, string symbol)
    {
        TraderId = traderId;
        Symbol = symbol;
    }
    
    public bool Equals(PositionKey other) =>
        TraderId == other.TraderId && Symbol == other.Symbol;
    
    public override bool Equals(object? obj) =>
        obj is PositionKey other && Equals(other);
    
    public override int GetHashCode() =>
        HashCode.Combine(TraderId, Symbol);
    
    public override string ToString() => $"{TraderId}:{Symbol}";
    
    public static bool operator ==(PositionKey left, PositionKey right) => left.Equals(right);
    public static bool operator !=(PositionKey left, PositionKey right) => !left.Equals(right);
}
