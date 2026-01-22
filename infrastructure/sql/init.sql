-- EOD Burst System - SQL Server Initialization Script
-- Run this script to create the database and tables for regulatory reporting

USE master;
GO

-- Create database if not exists
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'EodTrades')
BEGIN
    CREATE DATABASE EodTrades;
    PRINT 'Database EodTrades created.';
END
GO

USE EodTrades;
GO

-- ============================================
-- Reference Tables
-- ============================================

-- Traders reference table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Traders')
BEGIN
    CREATE TABLE dbo.Traders (
        TraderId        VARCHAR(20) PRIMARY KEY,
        TraderName      NVARCHAR(100) NOT NULL,
        Mpid            VARCHAR(20) NOT NULL,    -- Market Participant ID
        Crd             VARCHAR(20) NOT NULL,    -- Central Registration Depository
        Department      VARCHAR(50),
        IsActive        BIT NOT NULL DEFAULT 1,
        CreatedAt       DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        UpdatedAt       DATETIME2 NOT NULL DEFAULT GETUTCDATE()
    );
    
    PRINT 'Table Traders created.';
END
GO

-- Strategies reference table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Strategies')
BEGIN
    CREATE TABLE dbo.Strategies (
        StrategyCode    VARCHAR(20) PRIMARY KEY,
        StrategyName    NVARCHAR(100) NOT NULL,
        StrategyType    VARCHAR(50) NOT NULL,
        IsActive        BIT NOT NULL DEFAULT 1,
        CreatedAt       DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        UpdatedAt       DATETIME2 NOT NULL DEFAULT GETUTCDATE()
    );
    
    PRINT 'Table Strategies created.';
END
GO

-- Securities reference table  
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Securities')
BEGIN
    CREATE TABLE dbo.Securities (
        Symbol          VARCHAR(20) PRIMARY KEY,
        SecurityName    NVARCHAR(200) NOT NULL,
        Cusip           VARCHAR(9),
        Sedol           VARCHAR(7),
        Isin            VARCHAR(12),
        Exchange        VARCHAR(20),
        SecurityType    VARCHAR(50),
        IsActive        BIT NOT NULL DEFAULT 1,
        CreatedAt       DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        UpdatedAt       DATETIME2 NOT NULL DEFAULT GETUTCDATE()
    );
    
    PRINT 'Table Securities created.';
END
GO

-- ============================================
-- Main Trades Table (with Temporal/System-Versioning)
-- ============================================

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Trades')
BEGIN
    CREATE TABLE dbo.Trades (
        TradeId                 BIGINT IDENTITY(1,1) PRIMARY KEY,
        ExecId                  VARCHAR(50) NOT NULL,
        Symbol                  VARCHAR(20) NOT NULL,
        Quantity                BIGINT NOT NULL,
        Price                   DECIMAL(18, 8) NOT NULL,
        Side                    VARCHAR(20) NOT NULL,
        ExecTimestampUtc        DATETIME2 NOT NULL,
        
        -- Order linkage (for CAT reporting)
        OrderId                 VARCHAR(50) NOT NULL,
        ClOrdId                 VARCHAR(50) NOT NULL,
        
        -- Trader information
        TraderId                VARCHAR(20) NOT NULL,
        TraderName              NVARCHAR(100),
        TraderMpid              VARCHAR(20),
        
        -- Account information
        Account                 VARCHAR(50) NOT NULL,
        
        -- Strategy information
        StrategyCode            VARCHAR(20),
        StrategyName            NVARCHAR(100),
        
        -- Security information
        Cusip                   VARCHAR(9),
        
        -- Venue information
        Exchange                VARCHAR(20) NOT NULL,
        
        -- Audit trail
        SourceGatewayId         VARCHAR(100) NOT NULL,
        ReceiveTimestampUtc     DATETIME2 NOT NULL,
        EnrichmentTimestampUtc  DATETIME2 NOT NULL,
        CreatedAt               DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        
        -- Unique constraint on ExecId for idempotency
        CONSTRAINT UQ_Trades_ExecId UNIQUE (ExecId)
    );
    
    -- Indexes for common query patterns
    CREATE NONCLUSTERED INDEX IX_Trades_Symbol ON dbo.Trades (Symbol) INCLUDE (Quantity, Price, Side);
    CREATE NONCLUSTERED INDEX IX_Trades_TraderId ON dbo.Trades (TraderId) INCLUDE (Symbol, Quantity, Price);
    CREATE NONCLUSTERED INDEX IX_Trades_ExecTimestamp ON dbo.Trades (ExecTimestampUtc) INCLUDE (Symbol, TraderId);
    CREATE NONCLUSTERED INDEX IX_Trades_OrderId ON dbo.Trades (OrderId);
    CREATE NONCLUSTERED INDEX IX_Trades_CreatedAt ON dbo.Trades (CreatedAt);
    
    PRINT 'Table Trades created with indexes.';
END
GO

-- ============================================
-- Views for Reporting
-- ============================================

-- View: Daily trade summary by trader
CREATE OR ALTER VIEW dbo.vw_TraderDailySummary AS
SELECT 
    CAST(ExecTimestampUtc AS DATE) AS TradeDate,
    TraderId,
    TraderName,
    Symbol,
    SUM(CASE WHEN Side = 'BUY' THEN Quantity ELSE 0 END) AS BuyQuantity,
    SUM(CASE WHEN Side IN ('SELL', 'SELL_SHORT') THEN Quantity ELSE 0 END) AS SellQuantity,
    SUM(CASE WHEN Side = 'BUY' THEN Quantity ELSE -Quantity END) AS NetQuantity,
    COUNT(*) AS TradeCount,
    AVG(Price) AS AvgPrice,
    SUM(Quantity * Price) AS NotionalValue
FROM dbo.Trades
GROUP BY 
    CAST(ExecTimestampUtc AS DATE),
    TraderId,
    TraderName,
    Symbol;
GO

-- View: Hourly volume by symbol (for EOD burst analysis)
CREATE OR ALTER VIEW dbo.vw_HourlyVolume AS
SELECT 
    CAST(ExecTimestampUtc AS DATE) AS TradeDate,
    DATEPART(HOUR, ExecTimestampUtc) AS TradeHour,
    Symbol,
    SUM(Quantity) AS TotalQuantity,
    COUNT(*) AS TradeCount,
    AVG(Price) AS AvgPrice
FROM dbo.Trades
GROUP BY 
    CAST(ExecTimestampUtc AS DATE),
    DATEPART(HOUR, ExecTimestampUtc),
    Symbol;
GO

-- ============================================
-- Stored Procedures
-- ============================================

-- Procedure: Get trades for CAT reporting
CREATE OR ALTER PROCEDURE dbo.sp_GetTradesForCat
    @TradeDate DATE,
    @TraderId VARCHAR(20) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        ExecId,
        OrderId,
        ClOrdId,
        Symbol,
        Cusip,
        Side,
        Quantity,
        Price,
        ExecTimestampUtc,
        TraderId,
        TraderMpid,
        Account,
        Exchange,
        SourceGatewayId,
        ReceiveTimestampUtc
    FROM dbo.Trades
    WHERE CAST(ExecTimestampUtc AS DATE) = @TradeDate
      AND (@TraderId IS NULL OR TraderId = @TraderId)
    ORDER BY ExecTimestampUtc;
END
GO

-- Procedure: Get EOD position snapshot
CREATE OR ALTER PROCEDURE dbo.sp_GetEodPositions
    @TradeDate DATE
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        TraderId,
        TraderName,
        Symbol,
        SUM(CASE WHEN Side = 'BUY' THEN Quantity ELSE -Quantity END) AS NetPosition,
        SUM(CASE WHEN Side = 'BUY' THEN Quantity * Price ELSE 0 END) AS TotalBuyCost,
        SUM(CASE WHEN Side = 'BUY' THEN Quantity ELSE 0 END) AS TotalBuyQty,
        CASE 
            WHEN SUM(CASE WHEN Side = 'BUY' THEN Quantity ELSE 0 END) > 0 
            THEN SUM(CASE WHEN Side = 'BUY' THEN Quantity * Price ELSE 0 END) / 
                 SUM(CASE WHEN Side = 'BUY' THEN Quantity ELSE 0 END)
            ELSE 0 
        END AS AvgCost,
        COUNT(*) AS TradeCount
    FROM dbo.Trades
    WHERE CAST(ExecTimestampUtc AS DATE) = @TradeDate
    GROUP BY TraderId, TraderName, Symbol
    HAVING SUM(CASE WHEN Side = 'BUY' THEN Quantity ELSE -Quantity END) <> 0
    ORDER BY TraderId, Symbol;
END
GO

-- ============================================
-- Insert Reference Data
-- ============================================

-- Insert sample traders
MERGE INTO dbo.Traders AS target
USING (VALUES 
    ('T001', 'John Smith', 'MPID001', 'CRD001001', 'Equities'),
    ('T002', 'Jane Doe', 'MPID001', 'CRD001002', 'Equities'),
    ('T003', 'Bob Wilson', 'MPID001', 'CRD001003', 'Options'),
    ('T004', 'Alice Brown', 'MPID002', 'CRD002001', 'Quantitative'),
    ('T005', 'Charlie Davis', 'MPID002', 'CRD002002', 'Quantitative'),
    ('T006', 'Diana Lee', 'MPID002', 'CRD002003', 'Fixed Income'),
    ('T007', 'Edward Kim', 'MPID003', 'CRD003001', 'Derivatives'),
    ('T008', 'Fiona Chen', 'MPID003', 'CRD003002', 'Derivatives')
) AS source (TraderId, TraderName, Mpid, Crd, Department)
ON target.TraderId = source.TraderId
WHEN NOT MATCHED THEN
    INSERT (TraderId, TraderName, Mpid, Crd, Department)
    VALUES (source.TraderId, source.TraderName, source.Mpid, source.Crd, source.Department);

PRINT 'Reference data for Traders inserted.';
GO

-- Insert sample strategies
MERGE INTO dbo.Strategies AS target
USING (VALUES 
    ('VWAP', 'Volume Weighted Average Price', 'ALGO'),
    ('TWAP', 'Time Weighted Average Price', 'ALGO'),
    ('MOC', 'Market On Close', 'CLOSING'),
    ('LOC', 'Limit On Close', 'CLOSING'),
    ('IMPL', 'Implementation Shortfall', 'ALGO'),
    ('PAIRS', 'Pairs Trading', 'QUANT')
) AS source (StrategyCode, StrategyName, StrategyType)
ON target.StrategyCode = source.StrategyCode
WHEN NOT MATCHED THEN
    INSERT (StrategyCode, StrategyName, StrategyType)
    VALUES (source.StrategyCode, source.StrategyName, source.StrategyType);

PRINT 'Reference data for Strategies inserted.';
GO

PRINT 'EOD Burst database initialization complete.';
GO
