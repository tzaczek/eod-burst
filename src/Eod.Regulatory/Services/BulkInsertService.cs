using System.Data;
using Eod.Shared.Configuration;
using Eod.Shared.Models;
using FastMember;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Eod.Regulatory.Services;

/// <summary>
/// High-performance bulk insert service for SQL Server.
/// Uses SqlBulkCopy for maximum throughput.
/// </summary>
public sealed class BulkInsertService
{
    private readonly SqlServerSettings _settings;
    private readonly ILogger<BulkInsertService> _logger;
    
    private long _totalInserted;
    private long _totalErrors;
    private long _batchesProcessed;

    public BulkInsertService(
        IOptions<SqlServerSettings> settings,
        ILogger<BulkInsertService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Bulk inserts a batch of enriched trades.
    /// </summary>
    public async Task<int> BulkInsertAsync(
        IEnumerable<EnrichedTrade> trades,
        CancellationToken cancellationToken = default)
    {
        var tradeList = trades.ToList();
        if (tradeList.Count == 0)
            return 0;

        try
        {
            await using var connection = new SqlConnection(_settings.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            using var bulkCopy = new SqlBulkCopy(connection)
            {
                DestinationTableName = "dbo.Trades",
                BatchSize = _settings.BulkBatchSize,
                BulkCopyTimeout = _settings.BulkCopyTimeoutSeconds,
                EnableStreaming = _settings.EnableStreaming
            };

            // Map properties to columns
            ConfigureColumnMappings(bulkCopy);

            // Create DataReader from objects
            using var reader = ObjectReader.Create(
                tradeList,
                GetColumnNames());

            await bulkCopy.WriteToServerAsync(reader, cancellationToken);

            Interlocked.Add(ref _totalInserted, tradeList.Count);
            Interlocked.Increment(ref _batchesProcessed);

            _logger.LogDebug(
                "Bulk inserted {Count} trades. Total: {Total}",
                tradeList.Count,
                _totalInserted);

            return tradeList.Count;
        }
        catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
        {
            // Primary key or unique constraint violation
            // Fall back to merge/upsert for idempotency
            _logger.LogWarning(
                "Bulk insert conflict, falling back to merge. Count: {Count}",
                tradeList.Count);

            return await MergeInsertAsync(tradeList, cancellationToken);
        }
        catch (Exception ex)
        {
            Interlocked.Add(ref _totalErrors, tradeList.Count);
            _logger.LogError(ex, "Bulk insert failed for {Count} trades", tradeList.Count);
            throw;
        }
    }

    /// <summary>
    /// Idempotent merge/upsert for handling duplicates.
    /// </summary>
    private async Task<int> MergeInsertAsync(
        List<EnrichedTrade> trades,
        CancellationToken cancellationToken)
    {
        var insertedCount = 0;

        await using var connection = new SqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        foreach (var trade in trades)
        {
            try
            {
                await using var cmd = new SqlCommand(MergeSql, connection);
                cmd.CommandTimeout = _settings.CommandTimeoutSeconds;

                cmd.Parameters.AddWithValue("@ExecId", trade.ExecId);
                cmd.Parameters.AddWithValue("@Symbol", trade.Symbol);
                cmd.Parameters.AddWithValue("@Quantity", trade.Quantity);
                cmd.Parameters.AddWithValue("@Price", trade.Price);
                cmd.Parameters.AddWithValue("@Side", trade.Side);
                cmd.Parameters.AddWithValue("@ExecTimestampUtc", trade.ExecTimestampUtc);
                cmd.Parameters.AddWithValue("@OrderId", trade.OrderId);
                cmd.Parameters.AddWithValue("@ClOrdId", trade.ClOrdId);
                cmd.Parameters.AddWithValue("@TraderId", trade.TraderId);
                cmd.Parameters.AddWithValue("@TraderName", (object?)trade.TraderName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@TraderMpid", (object?)trade.TraderMpid ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Account", trade.Account);
                cmd.Parameters.AddWithValue("@StrategyCode", (object?)trade.StrategyCode ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@StrategyName", (object?)trade.StrategyName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Cusip", (object?)trade.Cusip ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Exchange", trade.Exchange);
                cmd.Parameters.AddWithValue("@SourceGatewayId", trade.SourceGatewayId);
                cmd.Parameters.AddWithValue("@ReceiveTimestampUtc", trade.ReceiveTimestampUtc);
                cmd.Parameters.AddWithValue("@EnrichmentTimestampUtc", trade.EnrichmentTimestampUtc);

                var result = await cmd.ExecuteNonQueryAsync(cancellationToken);
                if (result > 0)
                    insertedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to merge trade {ExecId}", trade.ExecId);
                Interlocked.Increment(ref _totalErrors);
            }
        }

        Interlocked.Add(ref _totalInserted, insertedCount);
        return insertedCount;
    }

    /// <summary>
    /// Ensures the database and tables exist.
    /// OBSOLETE: Schema is now managed by Entity Framework migrations via DatabaseMigrationService.
    /// This method is kept for backward compatibility but does nothing.
    /// </summary>
    [Obsolete("Schema is now managed by Entity Framework migrations. This method is a no-op.")]
    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("EnsureSchemaAsync is deprecated. Schema is managed by EF migrations.");
        return Task.CompletedTask;
    }

    private static void ConfigureColumnMappings(SqlBulkCopy bulkCopy)
    {
        bulkCopy.ColumnMappings.Add(nameof(EnrichedTrade.ExecId), "ExecId");
        bulkCopy.ColumnMappings.Add(nameof(EnrichedTrade.Symbol), "Symbol");
        bulkCopy.ColumnMappings.Add(nameof(EnrichedTrade.Quantity), "Quantity");
        bulkCopy.ColumnMappings.Add(nameof(EnrichedTrade.Price), "Price");
        bulkCopy.ColumnMappings.Add(nameof(EnrichedTrade.Side), "Side");
        bulkCopy.ColumnMappings.Add(nameof(EnrichedTrade.ExecTimestampUtc), "ExecTimestampUtc");
        bulkCopy.ColumnMappings.Add(nameof(EnrichedTrade.OrderId), "OrderId");
        bulkCopy.ColumnMappings.Add(nameof(EnrichedTrade.ClOrdId), "ClOrdId");
        bulkCopy.ColumnMappings.Add(nameof(EnrichedTrade.TraderId), "TraderId");
        bulkCopy.ColumnMappings.Add(nameof(EnrichedTrade.TraderName), "TraderName");
        bulkCopy.ColumnMappings.Add(nameof(EnrichedTrade.TraderMpid), "TraderMpid");
        bulkCopy.ColumnMappings.Add(nameof(EnrichedTrade.Account), "Account");
        bulkCopy.ColumnMappings.Add(nameof(EnrichedTrade.StrategyCode), "StrategyCode");
        bulkCopy.ColumnMappings.Add(nameof(EnrichedTrade.StrategyName), "StrategyName");
        bulkCopy.ColumnMappings.Add(nameof(EnrichedTrade.Cusip), "Cusip");
        bulkCopy.ColumnMappings.Add(nameof(EnrichedTrade.Exchange), "Exchange");
        bulkCopy.ColumnMappings.Add(nameof(EnrichedTrade.SourceGatewayId), "SourceGatewayId");
        bulkCopy.ColumnMappings.Add(nameof(EnrichedTrade.ReceiveTimestampUtc), "ReceiveTimestampUtc");
        bulkCopy.ColumnMappings.Add(nameof(EnrichedTrade.EnrichmentTimestampUtc), "EnrichmentTimestampUtc");
    }

    private static string[] GetColumnNames() =>
    [
        nameof(EnrichedTrade.ExecId),
        nameof(EnrichedTrade.Symbol),
        nameof(EnrichedTrade.Quantity),
        nameof(EnrichedTrade.Price),
        nameof(EnrichedTrade.Side),
        nameof(EnrichedTrade.ExecTimestampUtc),
        nameof(EnrichedTrade.OrderId),
        nameof(EnrichedTrade.ClOrdId),
        nameof(EnrichedTrade.TraderId),
        nameof(EnrichedTrade.TraderName),
        nameof(EnrichedTrade.TraderMpid),
        nameof(EnrichedTrade.Account),
        nameof(EnrichedTrade.StrategyCode),
        nameof(EnrichedTrade.StrategyName),
        nameof(EnrichedTrade.Cusip),
        nameof(EnrichedTrade.Exchange),
        nameof(EnrichedTrade.SourceGatewayId),
        nameof(EnrichedTrade.ReceiveTimestampUtc),
        nameof(EnrichedTrade.EnrichmentTimestampUtc)
    ];

    public long TotalInserted => Interlocked.Read(ref _totalInserted);
    public long TotalErrors => Interlocked.Read(ref _totalErrors);
    public long BatchesProcessed => Interlocked.Read(ref _batchesProcessed);

    // Note: Table creation is now handled by Entity Framework migrations in Eod.Shared.Data

    private const string MergeSql = """
        MERGE INTO dbo.Trades AS target
        USING (SELECT @ExecId AS ExecId) AS source
        ON target.ExecId = source.ExecId
        WHEN NOT MATCHED THEN
            INSERT (ExecId, Symbol, Quantity, Price, Side, ExecTimestampUtc, 
                    OrderId, ClOrdId, TraderId, TraderName, TraderMpid, 
                    Account, StrategyCode, StrategyName, Cusip, Exchange,
                    SourceGatewayId, ReceiveTimestampUtc, EnrichmentTimestampUtc)
            VALUES (@ExecId, @Symbol, @Quantity, @Price, @Side, @ExecTimestampUtc,
                    @OrderId, @ClOrdId, @TraderId, @TraderName, @TraderMpid,
                    @Account, @StrategyCode, @StrategyName, @Cusip, @Exchange,
                    @SourceGatewayId, @ReceiveTimestampUtc, @EnrichmentTimestampUtc);
        """;
}
