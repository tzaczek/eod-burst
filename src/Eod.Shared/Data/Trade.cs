using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Eod.Shared.Data;

/// <summary>
/// Trade entity for SQL Server persistence.
/// Maps to the dbo.Trades table.
/// </summary>
[Table("Trades", Schema = "dbo")]
public sealed class Trade
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long TradeId { get; set; }
    
    [Required]
    [MaxLength(50)]
    public required string ExecId { get; set; }
    
    [Required]
    [MaxLength(20)]
    public required string Symbol { get; set; }
    
    public required long Quantity { get; set; }
    
    [Column(TypeName = "decimal(18, 8)")]
    public required decimal Price { get; set; }
    
    [Required]
    [MaxLength(10)]
    public required string Side { get; set; }
    
    public required DateTime ExecTimestampUtc { get; set; }
    
    [Required]
    [MaxLength(50)]
    public required string OrderId { get; set; }
    
    [Required]
    [MaxLength(50)]
    public required string ClOrdId { get; set; }
    
    [Required]
    [MaxLength(20)]
    public required string TraderId { get; set; }
    
    [MaxLength(100)]
    public string? TraderName { get; set; }
    
    [MaxLength(20)]
    public string? TraderMpid { get; set; }
    
    [Required]
    [MaxLength(50)]
    public required string Account { get; set; }
    
    [MaxLength(20)]
    public string? StrategyCode { get; set; }
    
    [MaxLength(100)]
    public string? StrategyName { get; set; }
    
    [MaxLength(9)]
    public string? Cusip { get; set; }
    
    [Required]
    [MaxLength(20)]
    public required string Exchange { get; set; }
    
    [Required]
    [MaxLength(100)]
    public required string SourceGatewayId { get; set; }
    
    public required DateTime ReceiveTimestampUtc { get; set; }
    
    public required DateTime EnrichmentTimestampUtc { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
