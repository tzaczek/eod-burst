using Microsoft.EntityFrameworkCore;

namespace Eod.Shared.Data;

/// <summary>
/// Entity Framework Core DbContext for EOD Trades database.
/// </summary>
public class EodDbContext : DbContext
{
    public EodDbContext(DbContextOptions<EodDbContext> options)
        : base(options)
    {
    }

    public DbSet<Trade> Trades => Set<Trade>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Trade>(entity =>
        {
            // Unique constraint on ExecId
            entity.HasIndex(e => e.ExecId)
                .IsUnique()
                .HasDatabaseName("UQ_Trades_ExecId");

            // Indexes for common queries
            entity.HasIndex(e => e.Symbol)
                .HasDatabaseName("IX_Trades_Symbol");

            entity.HasIndex(e => e.TraderId)
                .HasDatabaseName("IX_Trades_TraderId");

            entity.HasIndex(e => e.ExecTimestampUtc)
                .HasDatabaseName("IX_Trades_ExecTimestamp");

            // Default value for CreatedAt
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");

            // Configure column types
            entity.Property(e => e.ExecTimestampUtc)
                .HasColumnType("datetime2");

            entity.Property(e => e.ReceiveTimestampUtc)
                .HasColumnType("datetime2");

            entity.Property(e => e.EnrichmentTimestampUtc)
                .HasColumnType("datetime2");

            entity.Property(e => e.CreatedAt)
                .HasColumnType("datetime2");
        });
    }
}
