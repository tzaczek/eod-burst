using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Eod.Shared.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateTable(
                name: "Trades",
                schema: "dbo",
                columns: table => new
                {
                    TradeId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExecId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Quantity = table.Column<long>(type: "bigint", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    Side = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ExecTimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OrderId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ClOrdId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TraderId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TraderName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TraderMpid = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Account = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StrategyCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    StrategyName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Cusip = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: true),
                    Exchange = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SourceGatewayId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ReceiveTimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EnrichmentTimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trades", x => x.TradeId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Trades_ExecTimestamp",
                schema: "dbo",
                table: "Trades",
                column: "ExecTimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_Symbol",
                schema: "dbo",
                table: "Trades",
                column: "Symbol");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_TraderId",
                schema: "dbo",
                table: "Trades",
                column: "TraderId");

            migrationBuilder.CreateIndex(
                name: "UQ_Trades_ExecId",
                schema: "dbo",
                table: "Trades",
                column: "ExecId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Trades",
                schema: "dbo");
        }
    }
}
