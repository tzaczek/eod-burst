namespace Eod.Shared.Models;

/// <summary>
/// Trade enriched with reference data for regulatory reporting.
/// This is the format that gets bulk-inserted to SQL Server.
/// </summary>
public sealed class EnrichedTrade
{
    // Primary identifier
    public required string ExecId { get; init; }
    
    // Trade details
    public required string Symbol { get; init; }
    public required long Quantity { get; init; }
    public required decimal Price { get; init; }
    public required string Side { get; init; }
    public required DateTime ExecTimestampUtc { get; init; }
    
    // Order linkage (for CAT reporting)
    public required string OrderId { get; init; }
    public required string ClOrdId { get; init; }
    
    // Enriched trader information
    public required string TraderId { get; init; }
    public string? TraderName { get; init; }
    public string? TraderMpid { get; init; }  // Market Participant ID
    public string? TraderCrd { get; init; }   // Central Registration Depository
    
    // Enriched account information
    public required string Account { get; init; }
    public string? AccountType { get; init; }  // "PROP", "CUSTOMER", "MARKET_MAKER"
    
    // Enriched strategy information
    public string? StrategyCode { get; init; }
    public string? StrategyName { get; init; }
    public string? StrategyType { get; init; }
    
    // Enriched security information
    public string? Cusip { get; init; }
    public string? Sedol { get; init; }
    public string? Isin { get; init; }
    public string? SecurityName { get; init; }
    
    // Venue information
    public required string Exchange { get; init; }
    public string? Mic { get; init; }  // Market Identifier Code
    
    // Audit trail
    public required string SourceGatewayId { get; init; }
    public required DateTime ReceiveTimestampUtc { get; init; }
    public required DateTime EnrichmentTimestampUtc { get; init; }
    
    // Raw data for compliance
    public byte[]? RawFixMessage { get; init; }
}
