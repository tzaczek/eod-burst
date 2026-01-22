# End of Day (EOD) Burst System Architecture

## Executive Summary

This document describes the architecture for an **End of Day Burst Processing System** designed to handle the extreme load spike that occurs at market close (4:00 PM EST). The system addresses the fundamental tension between **Speed** (Flash P&L for traders) and **Truth** (Regulatory compliance for SEC/FINRA).

---

## Table of Contents

1. [The Core Problem](#1-the-core-problem)
2. [Technology Stack](#2-technology-stack)
3. [High-Level Architecture](#3-high-level-architecture)
4. [Scenario A: Ingestion Firehose](#4-scenario-a-ingestion-firehose)
5. [Scenario B: Flash P&L Engine](#5-scenario-b-flash-pnl-engine)
6. [Scenario C: Regulatory Reporter](#6-scenario-c-regulatory-reporter)
7. [Docker Infrastructure](#7-docker-infrastructure)
8. [Scaling Strategy](#8-scaling-strategy)
9. [Improvements & Enhancements](#9-improvements--enhancements)
10. [Implementation Roadmap](#10-implementation-roadmap)

---

## 1. The Core Problem

### The Divergence of Speed and Truth

At 4:00:00 PM, the business needs two contradictory things simultaneously:

| Requirement | Priority | Tolerance | Use Case |
|-------------|----------|-----------|----------|
| **Flash P&L** | Speed | ~100ms latency, ~99% accuracy | Traders hedge overnight exposure |
| **Regulatory/Books** | Truth | Hours acceptable, 100% accuracy | SEC/FINRA compliance, audit trail |

### Why You Cannot Use a Single Pipeline

```
┌─────────────────────────────────────────────────────────────────────┐
│                    THE ARCHITECTURAL PARADOX                        │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  If you optimize for TRUTH (SQL ACID transactions):                │
│    → Row-level locking kills latency                               │
│    → 5M inserts = transaction log explosion                        │
│    → Traders see stale P&L during critical hedging window          │
│                                                                     │
│  If you optimize for SPEED (In-Memory/UDP):                        │
│    → Risk of data loss on crash                                    │
│    → No audit trail for regulators                                 │
│    → Cannot enforce referential integrity                          │
│                                                                     │
│  SOLUTION: CQRS Pattern - Split the pipeline at the source         │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 2. Technology Stack

### Core Technologies

| Component | Technology | Rationale |
|-----------|------------|-----------|
| **Backend** | .NET 8 C# | High performance, excellent async support, strong typing |
| **Messaging** | Apache Kafka | Durable, partitioned, replayable event log |
| **Cache** | Redis | Sub-millisecond reads, Pub/Sub for real-time updates |
| **Database** | SQL Server | ACID compliance, excellent bulk operations |
| **Object Storage** | MinIO | S3-compatible, local development friendly |
| **Serialization** | Protobuf | 10x smaller than JSON, schema evolution support |
| **Container Runtime** | Docker + Docker Compose | Local development with production parity |

### Why These Choices?

#### .NET 8 C# for Backend

```
Decision: Use .NET 8 with C# 12

Rationale:
├── Performance: Near-native speed with AOT compilation option
├── Async/Await: First-class support for I/O-bound operations (Kafka, Redis, SQL)
├── Memory: Span<T> and Memory<T> for zero-allocation parsing
├── Ecosystem: Confluent.Kafka, StackExchange.Redis are production-grade
└── Tooling: Excellent Docker support, cross-platform

Trade-offs Considered:
├── Go: Faster cold-start, but less mature financial libraries
├── Java: Mature ecosystem, but higher memory footprint
└── Rust: Maximum performance, but slower development velocity
```

#### Kafka for Messaging

```
Decision: Apache Kafka (not RabbitMQ, not AWS SQS)

Rationale:
├── Durability: Messages persisted to disk (survives broker restart)
├── Replay: Can re-read messages from any offset (critical for debugging)
├── Partitioning: Parallel processing with ordering guarantees per partition
├── Consumer Groups: Multiple services read same topic independently
└── Throughput: 1M+ messages/sec per broker

Why NOT RabbitMQ:
├── Messages deleted after acknowledgment (no replay)
├── Single consumer per queue (no parallel processing by design)
└── Memory-based (risk of data loss under pressure)

Why NOT AWS SQS:
├── 256KB message size limit
├── No ordering guarantees (FIFO queues have throughput limits)
└── Higher latency than self-hosted Kafka
```

#### Redis for Cache/Pub-Sub

```
Decision: Redis 7.x (not Memcached, not Hazelcast)

Rationale:
├── Data Structures: Hashes perfect for position state (HSET trader:123 AAPL 500)
├── Pub/Sub: Built-in for pushing updates to UI subscribers
├── Atomic Operations: HINCRBY for thread-safe position updates
├── Lua Scripts: Complex operations without round-trips
└── Persistence: RDB snapshots for recovery

Why NOT Memcached:
├── No data structures (only key-value strings)
├── No Pub/Sub capability
└── No persistence options
```

#### SQL Server for Regulatory Data

```
Decision: SQL Server 2022 (not PostgreSQL, not MongoDB)

Rationale:
├── SqlBulkCopy: Native .NET bulk insert (10x faster than row-by-row)
├── Temporal Tables: Built-in audit history (regulatory requirement)
├── Partitioning: Table partitioning by trade date for efficient queries
├── AWS Compatible: RDS for SQL Server available
└── Enterprise Support: Critical for financial systems

Why NOT PostgreSQL:
├── Less mature .NET bulk insert support
├── No native temporal tables (requires triggers)

Why NOT MongoDB:
├── Cannot enforce foreign key relationships
├── Regulatory data requires strict schema
```

---

## 3. High-Level Architecture

### The "Y" Split Pattern

```
                                    ┌─────────────────────────────────────────┐
                                    │         EXTERNAL SYSTEMS                │
                                    │  (Exchanges, Brokers, Dark Pools)       │
                                    └─────────────────┬───────────────────────┘
                                                      │
                                                      │ FIX Protocol
                                                      ▼
┌─────────────────────────────────────────────────────────────────────────────────────────┐
│                              SCENARIO A: INGESTION FIREHOSE                             │
│  ┌────────────────────────────────────────────────────────────────────────────────────┐ │
│  │                           FIX Gateway Service (.NET)                               │ │
│  │                                                                                    │ │
│  │   ┌─────────────┐    ┌─────────────┐    ┌─────────────┐    ┌─────────────┐        │ │
│  │   │ FIX Session │───▶│ Checksum    │───▶│ Protobuf    │───▶│ Kafka       │        │ │
│  │   │ Handler     │    │ Validation  │    │ Serializer  │    │ Producer    │        │ │
│  │   └─────────────┘    └─────────────┘    └──────┬──────┘    └─────────────┘        │ │
│  │                                                │                                   │ │
│  │                                                │ Async Tee                         │ │
│  │                                                ▼                                   │ │
│  │                                         ┌─────────────┐                            │ │
│  │                                         │ MinIO (S3)  │ ◀── Immutable Archive     │ │
│  │                                         │ Raw Bytes   │                            │ │
│  │                                         └─────────────┘                            │ │
│  └────────────────────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────────────────────┘
                                                      │
                                                      │ Kafka Topic: trades.raw
                                                      │ Partitioned by Symbol
                                                      ▼
                              ┌────────────────────────────────────────────┐
                              │                 KAFKA                       │
                              │  ┌──────────┬──────────┬──────────────────┐│
                              │  │ Part 0   │ Part 1   │ Part 2 ... N     ││
                              │  │ (A-F)    │ (G-M)    │ (N-Z)            ││
                              │  └──────────┴──────────┴──────────────────┘│
                              └────────────────────────────────────────────┘
                                          │                    │
                    ┌─────────────────────┘                    └─────────────────────┐
                    │                                                                │
                    │ Consumer Group: flash-pnl                                      │ Consumer Group: regulatory
                    ▼                                                                ▼
┌───────────────────────────────────────────┐          ┌───────────────────────────────────────────┐
│   SCENARIO B: HOT PATH (Flash P&L)        │          │   SCENARIO C: COLD PATH (Regulatory)      │
│                                           │          │                                           │
│  ┌─────────────────────────────────────┐  │          │  ┌─────────────────────────────────────┐  │
│  │      P&L Engine Service (.NET)      │  │          │  │   Regulatory Service (.NET)         │  │
│  │                                     │  │          │  │                                     │  │
│  │  ┌───────────────────────────────┐  │  │          │  │  ┌───────────────────────────────┐  │  │
│  │  │   In-Memory Position State    │  │  │          │  │  │   Reference Data Enrichment   │  │  │
│  │  │   Dictionary<(Trader,Symbol), │  │  │          │  │  │   - Trader Lookup             │  │  │
│  │  │             Position>         │  │  │          │  │  │   - Strategy Code Mapping     │  │  │
│  │  └───────────────────────────────┘  │  │          │  │  │   - Symbol Master Join        │  │  │
│  │              │                      │  │          │  │  └───────────────────────────────┘  │  │
│  │              ▼                      │  │          │  │              │                      │  │
│  │  ┌───────────────────────────────┐  │  │          │  │              ▼                      │  │
│  │  │   Waterfall Mark Logic        │  │  │          │  │  ┌───────────────────────────────┐  │  │
│  │  │   1. Official Close           │  │  │          │  │  │   Batch Buffer (5000 rows)    │  │  │
│  │  │   2. Last Traded Price        │  │  │          │  │  └───────────────────────────────┘  │  │
│  │  │   3. Bid/Ask Mid              │  │  │          │  │              │                      │  │
│  │  └───────────────────────────────┘  │  │          │  │              ▼                      │  │
│  │              │                      │  │          │  │  ┌───────────────────────────────┐  │  │
│  │              ▼                      │  │          │  │  │   SqlBulkCopy Insert          │  │  │
│  └─────────────────────────────────────┘  │          │  │  └───────────────────────────────┘  │  │
│              │                            │          │  └─────────────────────────────────────┘  │
│              ▼                            │          │              │                            │
│  ┌─────────────────────────────────────┐  │          │              ▼                            │
│  │           REDIS                     │  │          │  ┌─────────────────────────────────────┐  │
│  │  ┌─────────────┐  ┌──────────────┐  │  │          │  │         SQL SERVER                  │  │
│  │  │ Hash:       │  │ Pub/Sub:     │  │  │          │  │  ┌─────────────────────────────┐    │  │
│  │  │ positions   │  │ pnl-updates  │  │  │          │  │  │ Tables:                     │    │  │
│  │  │             │──▶              │  │  │          │  │  │ - Trades (partitioned)      │    │  │
│  │  └─────────────┘  └──────────────┘  │  │          │  │  │ - Orders                    │    │  │
│  └─────────────────────────────────────┘  │          │  │  │ - TraderAudit (temporal)    │    │  │
│              │                            │          │  │  └─────────────────────────────┘    │  │
│              ▼                            │          │  └─────────────────────────────────────┘  │
│  ┌─────────────────────────────────────┐  │          │              │                            │
│  │   Trader Dashboards                 │  │          │              ▼                            │
│  │   (Web / Excel / Mobile)            │  │          │  ┌─────────────────────────────────────┐  │
│  └─────────────────────────────────────┘  │          │  │   CAT/TRACE Reports (T+1)           │  │
│                                           │          │  └─────────────────────────────────────┘  │
│   Latency Target: < 100ms                 │          │   Latency Target: < 4 hours              │
│   Accuracy: ~99%                          │          │   Accuracy: 100%                         │
└───────────────────────────────────────────┘          └───────────────────────────────────────────┘
```

---

## 4. Scenario A: Ingestion Firehose

### The Problem

At 3:59:59 PM, message volume spikes **100x** above daily average due to:
- NYSE "D-Orders" (discretionary orders released at close)
- Closing crosses (MOC/LOC orders)
- Algorithmic liquidations

A standard database-backed application will experience **Resource Starvation**:
```
Database CPU → 100% → Ingestion Service Timeouts → FIX Disconnections → Financial Loss
```

### Design: "Log-First, Parse-Later"

```csharp
// Ingestion Service - Minimal Processing Path
public class FixIngestionService : BackgroundService
{
    private readonly IKafkaProducer<string, byte[]> _producer;
    private readonly IMinioClient _archiveClient;
    private readonly Channel<FixMessage> _archiveChannel;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var rawMessage in _fixSession.ReadAllAsync(ct))
        {
            // STEP 1: Validate checksum only (no parsing)
            if (!FixValidator.ValidateChecksum(rawMessage))
            {
                _metrics.IncrementInvalidChecksum();
                continue;
            }

            // STEP 2: Async archive to S3 (fire-and-forget with bounded channel)
            _ = _archiveChannel.Writer.TryWrite(rawMessage);

            // STEP 3: Extract partition key (symbol) with minimal parsing
            var symbol = FixParser.ExtractSymbol(rawMessage.Span);

            // STEP 4: Serialize to Protobuf and publish
            var envelope = new TradeEnvelope
            {
                RawFix = rawMessage.ToArray(),
                ReceiveTimestamp = Stopwatch.GetTimestamp(),
                GatewayId = Environment.MachineName
            };

            await _producer.ProduceAsync(
                topic: "trades.raw",
                key: symbol,
                value: envelope.ToByteArray(),
                ct);
        }
    }
}
```

### Key Design Decisions

#### Decision 1: Protocol Decoupling (FIX → Protobuf)

```
┌─────────────────────────────────────────────────────────────────────┐
│                     FIX vs PROTOBUF                                 │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  FIX Message (Text-based, ~500 bytes):                             │
│  8=FIX.4.2|9=178|35=8|49=SENDER|56=TARGET|34=123|52=20260121-      │
│  16:00:00.123|55=AAPL|54=1|38=1000|44=150.25|...                   │
│                                                                     │
│  Protobuf Message (Binary, ~80 bytes):                             │
│  [binary representation - 6x smaller]                              │
│                                                                     │
│  WHY THIS MATTERS:                                                 │
│  ├── Network: 6x less bandwidth to Kafka                           │
│  ├── Parsing: Binary decode is 10x faster than string split        │
│  ├── Schema: Protobuf enforces types at compile time               │
│  └── Evolution: Can add fields without breaking consumers          │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

**Protobuf Schema:**

```protobuf
// File: protos/trade_envelope.proto
syntax = "proto3";

package eod.trades;

message TradeEnvelope {
    bytes raw_fix = 1;              // Original message for replay
    int64 receive_timestamp = 2;    // Stopwatch ticks (nanosecond precision)
    string gateway_id = 3;          // Which ingestion instance received it
    
    // Parsed fields (extracted during ingestion)
    string symbol = 10;
    string exec_id = 11;
    int64 quantity = 12;
    int64 price_mantissa = 13;      // Price * 10^8 (avoid floating point)
    int32 price_exponent = 14;
}
```

#### Decision 2: The "Tee-Pipe" to S3 (MinIO)

```
┌─────────────────────────────────────────────────────────────────────┐
│                     WHY ARCHIVE RAW BYTES?                          │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  Problem: Exchange updates FIX spec → Our parser crashes on new    │
│           message type → We lose 10 minutes of trades              │
│                                                                     │
│  Solution: Archive BEFORE parsing                                  │
│                                                                     │
│  Implementation:                                                   │
│  ┌────────────┐     ┌────────────────┐     ┌─────────────────┐     │
│  │ FIX Socket │────▶│ Bounded Channel│────▶│ Background Task │     │
│  │            │     │ (10K buffer)   │     │ → MinIO/S3      │     │
│  └────────────┘     └────────────────┘     └─────────────────┘     │
│        │                                                           │
│        │ Main path continues without waiting                       │
│        ▼                                                           │
│  ┌────────────┐                                                    │
│  │ Kafka      │                                                    │
│  └────────────┘                                                    │
│                                                                     │
│  Recovery Scenario:                                                │
│  1. Download raw bytes from S3: s3://eod-archive/2026-01-21/       │
│  2. Replay through fixed parser                                    │
│  3. Republish to Kafka                                             │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

#### Decision 3: Partitioning Strategy (By Symbol)

```
┌─────────────────────────────────────────────────────────────────────┐
│                  KAFKA PARTITIONING STRATEGY                        │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  Partition by SYMBOL (not by Trader, not by Time)                  │
│                                                                     │
│  WHY:                                                              │
│  ├── All AAPL trades go to same partition                          │
│  ├── Consumer sees AAPL trades IN ORDER                            │
│  └── Critical for "Average Price" calculation                      │
│                                                                     │
│  EXAMPLE - Wrong (Random Partitioning):                            │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │ Consumer 1: AAPL Buy 100 @ 150.00                           │   │
│  │ Consumer 2: AAPL Buy 100 @ 150.10   (processed first!)      │   │
│  │                                                             │   │
│  │ Result: Race condition → Wrong average price                │   │
│  └─────────────────────────────────────────────────────────────┘   │
│                                                                     │
│  EXAMPLE - Correct (Symbol Partitioning):                          │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │ Partition 0: AAPL trades only → Single consumer → Ordered   │   │
│  │ Partition 1: MSFT trades only → Single consumer → Ordered   │   │
│  └─────────────────────────────────────────────────────────────┘   │
│                                                                     │
│  PARTITION COUNT DECISION:                                         │
│  ├── Too few (3): Cannot scale consumers beyond 3                  │
│  ├── Too many (1000): Overhead, uneven distribution                │
│  └── Sweet spot: 12-24 partitions (matches typical server count)   │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### Project Structure: Scenario A

```
src/
├── Eod.Ingestion/
│   ├── Program.cs
│   ├── Services/
│   │   ├── FixSessionHandler.cs
│   │   ├── IngestionService.cs
│   │   └── S3ArchiveService.cs
│   ├── Parsers/
│   │   ├── FixParser.cs
│   │   └── FixValidator.cs
│   ├── Kafka/
│   │   └── KafkaProducerService.cs
│   └── appsettings.json
```

---

## 5. Scenario B: Flash P&L Engine

### The Problem

500 traders need updated P&L within **100 milliseconds** of the close. A SQL round-trip takes 5-10ms. Processing 50,000 trades sequentially = 500 seconds = **8+ minutes**.

### Design: Stateful Stream Processing with Local Caching

```csharp
// Flash P&L Engine - In-Memory State Machine
public class FlashPnlService : BackgroundService
{
    // In-memory state - updated in nanoseconds
    private readonly ConcurrentDictionary<PositionKey, Position> _positions = new();
    private readonly ConcurrentDictionary<string, decimal> _prices = new();
    
    private readonly IRedisClient _redis;
    private readonly IKafkaConsumer<string, TradeEnvelope> _consumer;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var batch in _consumer.ConsumeAsync(ct))
        {
            foreach (var trade in batch)
            {
                // STEP 1: Update position (nanoseconds, no I/O)
                var key = new PositionKey(trade.TraderId, trade.Symbol);
                _positions.AddOrUpdate(key,
                    _ => new Position(trade),
                    (_, existing) => existing.ApplyTrade(trade));

                // STEP 2: Calculate P&L with waterfall mark
                var position = _positions[key];
                var price = GetMark(trade.Symbol);
                var pnl = position.CalculatePnl(price);

                // STEP 3: Publish aggregated update (not every trade)
                if (ShouldPublish(key))
                {
                    await PublishToRedisAsync(key, position, pnl);
                }
            }

            // Commit offset after processing batch
            _consumer.Commit();
        }
    }

    private decimal GetMark(string symbol)
    {
        // Waterfall marking logic
        if (_prices.TryGetValue($"close:{symbol}", out var closePrice))
            return closePrice;  // Official close (best)
        
        if (_prices.TryGetValue($"ltp:{symbol}", out var lastPrice))
            return lastPrice;   // Last traded price
        
        if (_prices.TryGetValue($"mid:{symbol}", out var midPrice))
            return midPrice;    // Bid/Ask midpoint (fallback)
        
        return _prices.GetOrAdd($"stale:{symbol}", 0m);  // Yesterday's close
    }
}
```

### Key Design Decisions

#### Decision 1: In-Memory "Materialized Views"

```
┌─────────────────────────────────────────────────────────────────────┐
│              IN-MEMORY STATE vs DATABASE QUERIES                    │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  Trader asks: "What is my AAPL position?"                          │
│                                                                     │
│  DATABASE APPROACH:                                                │
│  SELECT SUM(Quantity * CASE Side WHEN 'BUY' THEN 1 ELSE -1 END)   │
│  FROM Trades WHERE TraderId = 'T123' AND Symbol = 'AAPL';          │
│                                                                     │
│  Time: ~5ms (network + query + index scan)                         │
│  Under load: ~50ms (lock contention)                               │
│                                                                     │
│  IN-MEMORY APPROACH:                                               │
│  var position = _positions[(traderId: "T123", symbol: "AAPL")];    │
│  return position.NetQuantity;                                      │
│                                                                     │
│  Time: ~50 nanoseconds (dictionary lookup)                         │
│  Under load: ~50 nanoseconds (no locks with ConcurrentDictionary)  │
│                                                                     │
│  TRADE-OFF:                                                        │
│  ├── Memory: ~1KB per position × 50,000 positions = 50MB           │
│  ├── Startup: Must replay from Kafka to rebuild state              │
│  └── Crash: State lost (acceptable for Flash P&L, not for Reg)     │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

#### Decision 2: Redis as Publishing Layer

```
┌─────────────────────────────────────────────────────────────────────┐
│                    REDIS PUBLISHING PATTERN                         │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  WHY REDIS (not WebSockets from C# service directly):              │
│                                                                     │
│  Problem: Traders use different UIs                                │
│  ├── Web Dashboard (React)                                         │
│  ├── Excel Add-in (RTD)                                            │
│  ├── Mobile App (React Native)                                     │
│  └── Bloomberg Terminal Integration                                │
│                                                                     │
│  Each UI would need direct connection to C# service = N×M problem  │
│                                                                     │
│  Solution: Redis as shared state + Pub/Sub                         │
│                                                                     │
│  ┌────────────┐     ┌─────────────┐     ┌────────────────────┐     │
│  │ P&L Engine │────▶│ Redis       │────▶│ UI Subscriber 1    │     │
│  │ (C#)       │     │ HSET + PUB  │     │ (Web Dashboard)    │     │
│  └────────────┘     │             │────▶│ UI Subscriber 2    │     │
│                     │             │     │ (Excel RTD)        │     │
│                     │             │────▶│ UI Subscriber 3    │     │
│                     └─────────────┘     │ (Mobile)           │     │
│                                         └────────────────────┘     │
│                                                                     │
│  DATA STRUCTURES:                                                  │
│  ├── HSET positions:T123 AAPL "100" MSFT "-50"                     │
│  ├── HSET pnl:T123 total "125000.50"                               │
│  └── PUBLISH pnl-updates:T123 {json payload}                       │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

#### Decision 3: Waterfall Marking Logic

```
┌─────────────────────────────────────────────────────────────────────┐
│                    PRICE WATERFALL LOGIC                            │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  PROBLEM: At 4:00 PM, "official close" may not arrive until 4:02   │
│                                                                     │
│  TIMELINE:                                                         │
│  ├── 4:00:00 - Last trade executes at $150.10                      │
│  ├── 4:00:30 - Traders need P&L NOW for hedging decision           │
│  ├── 4:01:45 - Exchange publishes official close: $150.15          │
│  └── 4:02:00 - P&L recalculates with official price                │
│                                                                     │
│  WATERFALL PRIORITY:                                               │
│  1. Official Close Price      ← Best (from exchange feed)          │
│  2. Last Traded Price (LTP)   ← Good (from our own trades)         │
│  3. Bid/Ask Midpoint          ← Acceptable (from market data)      │
│  4. Yesterday's Close         ← Stale (last resort)                │
│                                                                     │
│  IMPLEMENTATION:                                                   │
│  ```csharp                                                         │
│  decimal GetMark(string symbol) =>                                 │
│      _officialClose.TryGet(symbol, out var p1) ? p1 :              │
│      _lastTraded.TryGet(symbol, out var p2) ? p2 :                 │
│      _bidAskMid.TryGet(symbol, out var p3) ? p3 :                  │
│      _yesterdayClose[symbol];                                      │
│  ```                                                               │
│                                                                     │
│  AUDIT TRAIL: Log which price source was used for each position    │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### Project Structure: Scenario B

```
src/
├── Eod.FlashPnl/
│   ├── Program.cs
│   ├── Services/
│   │   ├── FlashPnlService.cs
│   │   ├── PositionAggregator.cs
│   │   └── PriceWaterfallService.cs
│   ├── Models/
│   │   ├── Position.cs
│   │   ├── PositionKey.cs
│   │   └── PnlSnapshot.cs
│   ├── Redis/
│   │   ├── RedisPublisher.cs
│   │   └── RedisStateStore.cs
│   ├── Kafka/
│   │   └── TradeConsumer.cs
│   └── appsettings.json
```

---

## 6. Scenario C: Regulatory Reporter

### The Problem

Regulatory reporting requires **Entity Relationship Linking**:
- Order (Parent) → Route (Child) → Execution (Grandchild)
- Must link across time and systems
- **Speed is irrelevant; Accuracy is mandatory**

If this runs in the same pipeline as Flash P&L, the complex joins will delay trader dashboards.

### Design: Asynchronous Batch Enrichment

```csharp
// Regulatory Reporter - Batch Processing
public class RegulatoryService : BackgroundService
{
    private readonly IKafkaConsumer<string, TradeEnvelope> _consumer;
    private readonly IReferenceDataService _refData;
    private readonly ISqlConnection _sql;
    
    private readonly List<EnrichedTrade> _buffer = new(5000);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var message in _consumer.ConsumeAsync(ct))
        {
            // STEP 1: Enrich with reference data
            var enriched = await EnrichTradeAsync(message.Value);
            _buffer.Add(enriched);

            // STEP 2: Bulk insert when buffer is full
            if (_buffer.Count >= 5000)
            {
                await BulkInsertAsync(_buffer, ct);
                _buffer.Clear();
                _consumer.Commit();
            }
        }
    }

    private async Task<EnrichedTrade> EnrichTradeAsync(TradeEnvelope trade)
    {
        // These lookups are SLOW but ACCURATE
        var trader = await _refData.GetTraderAsync(trade.TraderId);
        var strategy = await _refData.GetStrategyAsync(trade.StrategyCode);
        var security = await _refData.GetSecurityMasterAsync(trade.Symbol);

        return new EnrichedTrade
        {
            // Original fields
            ExecId = trade.ExecId,
            Symbol = trade.Symbol,
            Quantity = trade.Quantity,
            Price = trade.Price,
            
            // Enriched fields
            TraderName = trader.FullName,
            TraderMpid = trader.Mpid,
            StrategyName = strategy.Name,
            Cusip = security.Cusip,
            Sedol = security.Sedol,
            
            // Audit fields
            EnrichmentTimestamp = DateTime.UtcNow,
            SourceGateway = trade.GatewayId
        };
    }

    private async Task BulkInsertAsync(List<EnrichedTrade> trades, CancellationToken ct)
    {
        using var bulkCopy = new SqlBulkCopy(_sql)
        {
            DestinationTableName = "dbo.Trades",
            BatchSize = 5000,
            BulkCopyTimeout = 60
        };

        // Map properties to columns
        bulkCopy.ColumnMappings.Add(nameof(EnrichedTrade.ExecId), "ExecId");
        bulkCopy.ColumnMappings.Add(nameof(EnrichedTrade.Symbol), "Symbol");
        // ... more mappings

        using var reader = ObjectReader.Create(trades);
        await bulkCopy.WriteToServerAsync(reader, ct);
    }
}
```

### Key Design Decisions

#### Decision 1: Separate Consumer Group

```
┌─────────────────────────────────────────────────────────────────────┐
│                  KAFKA CONSUMER GROUPS                              │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  Kafka Topic: trades.raw                                           │
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │ Partition 0 │ Partition 1 │ Partition 2 │ ... │ Partition N  │  │
│  └──────────────────────────────────────────────────────────────┘  │
│           │              │              │                          │
│           ▼              ▼              ▼                          │
│  ┌────────────────────────────────────────────────────────────┐    │
│  │ Consumer Group: "flash-pnl"                                │    │
│  │ Offset: 1,000,000 (real-time)                              │    │
│  │ Lag: 0 messages                                            │    │
│  └────────────────────────────────────────────────────────────┘    │
│                                                                     │
│           │              │              │                          │
│           ▼              ▼              ▼                          │
│  ┌────────────────────────────────────────────────────────────┐    │
│  │ Consumer Group: "regulatory"                               │    │
│  │ Offset: 800,000 (20 minutes behind)                        │    │
│  │ Lag: 200,000 messages                                      │    │
│  │                                                            │    │
│  │ THIS IS OK! Traders are not affected.                      │    │
│  └────────────────────────────────────────────────────────────┘    │
│                                                                     │
│  WHY SEPARATE GROUPS:                                              │
│  ├── Flash P&L: Must stay real-time (lag = bad)                    │
│  ├── Regulatory: Can fall behind (lag = acceptable)                │
│  └── Independence: One crashing doesn't affect the other           │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

#### Decision 2: SQL Server with Temporal Tables

```sql
-- Regulatory Trade Table with Temporal History
CREATE TABLE dbo.Trades
(
    TradeId         BIGINT IDENTITY PRIMARY KEY,
    ExecId          VARCHAR(50) NOT NULL UNIQUE,
    Symbol          VARCHAR(20) NOT NULL,
    Quantity        BIGINT NOT NULL,
    Price           DECIMAL(18, 8) NOT NULL,
    TraderId        INT NOT NULL REFERENCES dbo.Traders(TraderId),
    StrategyId      INT NOT NULL REFERENCES dbo.Strategies(StrategyId),
    
    -- Audit columns (auto-managed by temporal)
    ValidFrom       DATETIME2 GENERATED ALWAYS AS ROW START,
    ValidTo         DATETIME2 GENERATED ALWAYS AS ROW END,
    
    PERIOD FOR SYSTEM_TIME (ValidFrom, ValidTo)
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.TradesHistory));

-- Partition by TradeDate for efficient queries
CREATE PARTITION FUNCTION PF_TradeDate (DATE)
AS RANGE RIGHT FOR VALUES ('2026-01-01', '2026-02-01', '2026-03-01');
```

```
┌─────────────────────────────────────────────────────────────────────┐
│                    WHY TEMPORAL TABLES?                             │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  REGULATORY REQUIREMENT: "Show me the trade as it appeared at       │
│  5:00 PM on January 21, 2026"                                      │
│                                                                     │
│  WITHOUT TEMPORAL:                                                 │
│  - Must implement trigger-based audit logging                      │
│  - Complex queries with effective dates                            │
│  - Easy to have bugs in audit logic                                │
│                                                                     │
│  WITH TEMPORAL:                                                    │
│  ```sql                                                            │
│  SELECT * FROM dbo.Trades                                          │
│  FOR SYSTEM_TIME AS OF '2026-01-21 17:00:00'                       │
│  WHERE ExecId = 'E123456';                                         │
│  ```                                                               │
│                                                                     │
│  SQL Server automatically:                                         │
│  ├── Creates history table                                         │
│  ├── Tracks all changes with timestamps                            │
│  └── Provides point-in-time query syntax                           │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

#### Decision 3: SqlBulkCopy for High Throughput

```
┌─────────────────────────────────────────────────────────────────────┐
│              ROW-BY-ROW vs BULK INSERT                              │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  SCENARIO: Insert 5,000,000 trades                                 │
│                                                                     │
│  ROW-BY-ROW (Entity Framework):                                    │
│  foreach (var trade in trades)                                     │
│      await _context.Trades.AddAsync(trade);                        │
│  await _context.SaveChangesAsync();                                │
│                                                                     │
│  Time: ~5ms per row × 5M = 7 HOURS                                 │
│  Transaction log: Explodes (each INSERT is logged)                 │
│                                                                     │
│  BULK INSERT (SqlBulkCopy):                                        │
│  using var bulkCopy = new SqlBulkCopy(connection);                 │
│  bulkCopy.BatchSize = 5000;                                        │
│  await bulkCopy.WriteToServerAsync(reader);                        │
│                                                                     │
│  Time: ~1000 rows/ms = 5M in ~83 MINUTES                           │
│  Transaction log: Minimal (batch-level logging)                    │
│                                                                     │
│  TRADE-OFF:                                                        │
│  ├── Pro: 5x faster than row-by-row                                │
│  ├── Con: All-or-nothing per batch (5000 rows)                     │
│  └── Con: Less granular error handling                             │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### Project Structure: Scenario C

```
src/
├── Eod.Regulatory/
│   ├── Program.cs
│   ├── Services/
│   │   ├── RegulatoryService.cs
│   │   ├── TradeEnricher.cs
│   │   └── BulkInsertService.cs
│   ├── Models/
│   │   ├── EnrichedTrade.cs
│   │   └── ReferenceData/
│   │       ├── Trader.cs
│   │       ├── Strategy.cs
│   │       └── Security.cs
│   ├── Data/
│   │   ├── ReferenceDataRepository.cs
│   │   └── TradeRepository.cs
│   └── appsettings.json
```

---

## 7. Docker Infrastructure

### Docker Compose Architecture

```yaml
# docker-compose.yml
version: '3.8'

services:
  # ============================================
  # INFRASTRUCTURE SERVICES
  # ============================================
  
  zookeeper:
    image: confluentinc/cp-zookeeper:7.5.0
    environment:
      ZOOKEEPER_CLIENT_PORT: 2181
    healthcheck:
      test: echo srvr | nc localhost 2181 || exit 1
      interval: 10s
      timeout: 5s
      retries: 5

  kafka:
    image: confluentinc/cp-kafka:7.5.0
    depends_on:
      zookeeper:
        condition: service_healthy
    ports:
      - "9092:9092"
    environment:
      KAFKA_BROKER_ID: 1
      KAFKA_ZOOKEEPER_CONNECT: zookeeper:2181
      KAFKA_ADVERTISED_LISTENERS: PLAINTEXT://kafka:29092,PLAINTEXT_HOST://localhost:9092
      KAFKA_LISTENER_SECURITY_PROTOCOL_MAP: PLAINTEXT:PLAINTEXT,PLAINTEXT_HOST:PLAINTEXT
      KAFKA_INTER_BROKER_LISTENER_NAME: PLAINTEXT
      KAFKA_NUM_PARTITIONS: 12
      KAFKA_DEFAULT_REPLICATION_FACTOR: 1
      KAFKA_AUTO_CREATE_TOPICS_ENABLE: "true"
    healthcheck:
      test: kafka-broker-api-versions --bootstrap-server localhost:9092
      interval: 10s
      timeout: 5s
      retries: 5

  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"
    command: redis-server --appendonly yes --maxmemory 256mb --maxmemory-policy allkeys-lru
    volumes:
      - redis-data:/data
    healthcheck:
      test: redis-cli ping
      interval: 5s
      timeout: 3s
      retries: 5

  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    ports:
      - "1433:1433"
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: "YourStrong@Passw0rd"
      MSSQL_PID: "Developer"
    volumes:
      - sqlserver-data:/var/opt/mssql
    healthcheck:
      test: /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "YourStrong@Passw0rd" -Q "SELECT 1"
      interval: 10s
      timeout: 5s
      retries: 5

  minio:
    image: minio/minio:latest
    ports:
      - "9000:9000"
      - "9001:9001"
    environment:
      MINIO_ROOT_USER: minioadmin
      MINIO_ROOT_PASSWORD: minioadmin
    command: server /data --console-address ":9001"
    volumes:
      - minio-data:/data
    healthcheck:
      test: curl -f http://localhost:9000/minio/health/live
      interval: 10s
      timeout: 5s
      retries: 5

  # ============================================
  # APPLICATION SERVICES
  # ============================================

  ingestion:
    build:
      context: ./src/Eod.Ingestion
      dockerfile: Dockerfile
    depends_on:
      kafka:
        condition: service_healthy
      minio:
        condition: service_healthy
    environment:
      - KAFKA_BOOTSTRAP_SERVERS=kafka:29092
      - MINIO_ENDPOINT=minio:9000
      - ASPNETCORE_ENVIRONMENT=Development
    deploy:
      replicas: 2  # Scale for HA
      resources:
        limits:
          cpus: '1'
          memory: 512M

  flash-pnl:
    build:
      context: ./src/Eod.FlashPnl
      dockerfile: Dockerfile
    depends_on:
      kafka:
        condition: service_healthy
      redis:
        condition: service_healthy
    environment:
      - KAFKA_BOOTSTRAP_SERVERS=kafka:29092
      - KAFKA_CONSUMER_GROUP=flash-pnl
      - REDIS_CONNECTION=redis:6379
      - ASPNETCORE_ENVIRONMENT=Development
    deploy:
      replicas: 3  # Scale for throughput
      resources:
        limits:
          cpus: '2'
          memory: 1G

  regulatory:
    build:
      context: ./src/Eod.Regulatory
      dockerfile: Dockerfile
    depends_on:
      kafka:
        condition: service_healthy
      sqlserver:
        condition: service_healthy
    environment:
      - KAFKA_BOOTSTRAP_SERVERS=kafka:29092
      - KAFKA_CONSUMER_GROUP=regulatory
      - SQL_CONNECTION=Server=sqlserver;Database=EodTrades;User=sa;Password=YourStrong@Passw0rd;
      - ASPNETCORE_ENVIRONMENT=Development
    deploy:
      replicas: 2  # Scale for throughput
      resources:
        limits:
          cpus: '2'
          memory: 2G

volumes:
  redis-data:
  sqlserver-data:
  minio-data:

networks:
  default:
    name: eod-network
```

### Service Dockerfile Template

```dockerfile
# src/Eod.Ingestion/Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY *.csproj ./
RUN dotnet restore

# Copy source and build
COPY . ./
RUN dotnet publish -c Release -o /app

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Health check endpoint
HEALTHCHECK --interval=30s --timeout=3s \
  CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Eod.Ingestion.dll"]
```

---

## 8. Scaling Strategy

### Local Docker Scaling with Docker Compose

```
┌─────────────────────────────────────────────────────────────────────┐
│                   LOCAL SCALING OPTIONS                             │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  OPTION 1: Docker Compose Replicas (Simplest)                      │
│  ```bash                                                           │
│  # Scale up during EOD burst                                       │
│  docker compose up -d --scale flash-pnl=5 --scale regulatory=3     │
│                                                                     │
│  # Scale down after burst                                          │
│  docker compose up -d --scale flash-pnl=1 --scale regulatory=1     │
│  ```                                                               │
│                                                                     │
│  OPTION 2: Docker Swarm Mode (Better orchestration)                │
│  ```bash                                                           │
│  # Initialize swarm                                                │
│  docker swarm init                                                 │
│                                                                     │
│  # Deploy stack                                                    │
│  docker stack deploy -c docker-compose.yml eod                     │
│                                                                     │
│  # Scale service                                                   │
│  docker service scale eod_flash-pnl=5                              │
│  ```                                                               │
│                                                                     │
│  OPTION 3: Kubernetes (k3d) for Production Parity                  │
│  ```bash                                                           │
│  # Create local K8s cluster                                        │
│  k3d cluster create eod-cluster --servers 1 --agents 3             │
│                                                                     │
│  # Apply Horizontal Pod Autoscaler                                 │
│  kubectl apply -f k8s/hpa.yaml                                     │
│  ```                                                               │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### Kafka Consumer Scaling

```
┌─────────────────────────────────────────────────────────────────────┐
│             KAFKA CONSUMER SCALING FUNDAMENTALS                     │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  RULE: Max parallelism = Number of partitions                      │
│                                                                     │
│  Example: Topic "trades.raw" has 12 partitions                     │
│                                                                     │
│  Scenario 1: 1 Consumer (Normal hours)                             │
│  ┌───────────────────────────────────────────────────────────┐     │
│  │ Consumer 1: Partitions 0,1,2,3,4,5,6,7,8,9,10,11          │     │
│  └───────────────────────────────────────────────────────────┘     │
│                                                                     │
│  Scenario 2: 3 Consumers (EOD burst)                               │
│  ┌───────────────────────────────────────────────────────────┐     │
│  │ Consumer 1: Partitions 0,1,2,3                            │     │
│  │ Consumer 2: Partitions 4,5,6,7                            │     │
│  │ Consumer 3: Partitions 8,9,10,11                          │     │
│  └───────────────────────────────────────────────────────────┘     │
│                                                                     │
│  Scenario 3: 15 Consumers (WASTEFUL!)                              │
│  ┌───────────────────────────────────────────────────────────┐     │
│  │ Consumers 1-12: One partition each                        │     │
│  │ Consumers 13-15: IDLE (no partitions to consume)          │     │
│  └───────────────────────────────────────────────────────────┘     │
│                                                                     │
│  RECOMMENDATION:                                                   │
│  - Create 12-24 partitions for trades.raw                          │
│  - Run 2-3 consumers during normal hours                           │
│  - Scale to 12 consumers during EOD burst                          │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### Resource Limits and Burst Handling

```yaml
# docker-compose.override.burst.yml
# Use: docker compose -f docker-compose.yml -f docker-compose.override.burst.yml up

services:
  flash-pnl:
    deploy:
      replicas: 6
      resources:
        limits:
          cpus: '4'
          memory: 2G
        reservations:
          cpus: '2'
          memory: 1G

  regulatory:
    deploy:
      replicas: 4
      resources:
        limits:
          cpus: '4'
          memory: 4G
        reservations:
          cpus: '2'
          memory: 2G

  kafka:
    environment:
      # Increase for burst handling
      KAFKA_NUM_IO_THREADS: 8
      KAFKA_NUM_NETWORK_THREADS: 6
      KAFKA_LOG_FLUSH_INTERVAL_MESSAGES: 10000
```

---

## 9. Improvements & Enhancements

### Enhancement 1: Circuit Breaker Pattern (IMPLEMENTED)

```
┌─────────────────────────────────────────────────────────────────────┐
│                    CIRCUIT BREAKER                                  │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  PROBLEM: Redis/S3/External service goes down → Service hangs →   │
│           Kafka consumer stalls → Consumer group rebalances →      │
│           Cascade failure across entire system                     │
│                                                                     │
│  SOLUTION: Custom Circuit Breaker Implementation                   │
│            (src/Eod.Shared/Resilience/)                            │
│                                                                     │
│  STATE MACHINE:                                                    │
│                                                                     │
│              ┌─────────────────────────────────────────┐           │
│              │                                         │           │
│              ▼                                         │           │
│    ┌──────────────┐    Failure Threshold    ┌─────────┴────┐      │
│    │              │        Exceeded         │              │      │
│    │    CLOSED    │ ──────────────────────► │     OPEN     │      │
│    │              │                         │              │      │
│    └──────────────┘                         └───────┬──────┘      │
│            ▲                                        │             │
│            │                                        │ Open Duration│
│            │                                        │   Expires   │
│            │                                        ▼             │
│            │       Success Threshold      ┌──────────────┐        │
│            │         Reached              │              │        │
│            └───────────────────────────── │  HALF-OPEN   │        │
│                                           │              │        │
│              Failure                      └──────────────┘        │
│              Occurs                              │                │
│                 ┌────────────────────────────────┘                │
│                 │                                                 │
│                 ▼                                                 │
│           Back to OPEN                                            │
│                                                                   │
└───────────────────────────────────────────────────────────────────┘
```

#### Implementation Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                 CIRCUIT BREAKER COMPONENTS                          │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  src/Eod.Shared/Resilience/                                        │
│  ├── ICircuitBreaker.cs       # Interface (DIP compliance)         │
│  ├── CircuitBreaker.cs        # Thread-safe implementation         │
│  ├── CircuitBreakerState.cs   # Enum: Closed, Open, HalfOpen       │
│  ├── CircuitBreakerOptions.cs # Configuration with presets         │
│  └── CircuitBreakerFactory.cs # Factory pattern for management     │
│                                                                     │
│  DESIGN PATTERNS APPLIED:                                          │
│  ├── Strategy Pattern: Different options for different scenarios   │
│  ├── Factory Pattern: CircuitBreakerFactory manages instances      │
│  ├── Observer Pattern: StateChanged event for monitoring           │
│  └── DIP: Services depend on ICircuitBreaker abstraction           │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

#### Configuration Presets

```csharp
// Three built-in presets for common scenarios:

// 1. High Availability - Aggressive settings for internal services
CircuitBreakerOptions.HighAvailability
├── FailureThreshold: 3
├── OpenDuration: 15 seconds
├── SuccessThresholdInHalfOpen: 1
└── FailureWindow: 30 seconds

// 2. External Service - Conservative for external APIs
CircuitBreakerOptions.ExternalService
├── FailureThreshold: 5
├── OpenDuration: 60 seconds
├── SuccessThresholdInHalfOpen: 3
└── FailureWindow: 120 seconds

// 3. Storage - Balanced for S3/Database operations
CircuitBreakerOptions.Storage
├── FailureThreshold: 10
├── OpenDuration: 30 seconds
├── SuccessThresholdInHalfOpen: 2
└── FailureWindow: 60 seconds
```

#### Usage Example: S3 Archive Service

```csharp
// S3ArchiveService.cs - Protected archive operations
public sealed class S3ArchiveService : BackgroundService
{
    private readonly ICircuitBreaker _circuitBreaker;
    
    public S3ArchiveService(ICircuitBreakerFactory circuitBreakerFactory, ...)
    {
        _circuitBreaker = circuitBreakerFactory.GetOrCreate(
            "S3Archive",
            CircuitBreakerOptions.Storage with 
            { 
                Name = "S3Archive",
                ExceptionTypes = [typeof(MinioException), typeof(HttpRequestException)]
            });
    }
    
    private async Task FlushBufferAsync(CancellationToken ct)
    {
        // Fast fail if circuit is open - don't block on failing service
        if (_circuitBreaker.State == CircuitBreakerState.Open)
        {
            _logger.LogWarning("Circuit open - dropping messages");
            return;
        }

        await _circuitBreaker.ExecuteAsync(async token =>
        {
            await _minioClient.PutObjectAsync(..., token);
        }, ct);
    }
}
```

#### Usage Example: Metrics Collector

```csharp
// IngestionMetricsCollector.cs - Protected HTTP calls
public sealed class IngestionMetricsCollector : IMetricsCollector<IngestionMetrics>
{
    private readonly ICircuitBreaker _circuitBreaker;
    
    public async Task<IngestionMetrics> CollectAsync(CancellationToken ct)
    {
        // Return cached/default when circuit is open
        if (_circuitBreaker.State == CircuitBreakerState.Open)
            return new IngestionMetrics { Status = "circuit-open" };
        
        return await _circuitBreaker.ExecuteAsync(async token =>
        {
            var response = await _httpClient.GetAsync("/metrics", token);
            return ParseMetrics(response);
        }, ct);
    }
}
```

#### Key Features

```
┌─────────────────────────────────────────────────────────────────────┐
│                    FEATURE HIGHLIGHTS                               │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  1. SLIDING WINDOW: Only counts failures within configurable time  │
│     window - old failures don't trip the circuit                   │
│                                                                     │
│  2. EXCEPTION FILTERING: Can trip only on specific exception types │
│     _circuitBreaker = factory.GetOrCreate("S3", options with {     │
│         ExceptionTypes = [typeof(MinioException)]  // Only these   │
│     });                                                            │
│                                                                     │
│  3. METRICS & OBSERVABILITY:                                       │
│     var metrics = _circuitBreaker.Metrics;                         │
│     metrics.TotalRequests      // Total calls through breaker      │
│     metrics.SuccessfulRequests // Successful executions            │
│     metrics.FailedRequests     // Failed executions                │
│     metrics.RejectedRequests   // Rejected when open               │
│     metrics.SuccessRate        // Percentage (0-100)               │
│     metrics.TimeUntilHalfOpen  // Countdown to recovery test       │
│                                                                     │
│  4. EVENT NOTIFICATIONS:                                           │
│     _circuitBreaker.StateChanged += (sender, args) => {            │
│         Log.Warning("Circuit {Name}: {From} → {To}",               │
│             args.CircuitBreakerName,                               │
│             args.PreviousState,                                    │
│             args.NewState);                                        │
│     };                                                             │
│                                                                     │
│  5. MANUAL CONTROL:                                                │
│     _circuitBreaker.Trip();  // Force open (maintenance mode)      │
│     _circuitBreaker.Reset(); // Force closed (recovery confirmed)  │
│                                                                     │
│  6. THREAD-SAFE: Lock-free counters using Interlocked operations   │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

#### DI Registration

```csharp
// Program.cs - Register circuit breaker factory
builder.Services.AddSingleton<ICircuitBreakerFactory, CircuitBreakerFactory>();

// Services automatically get named circuit breakers via factory
// Each service creates its own with appropriate settings
```

#### Unit Test Coverage (45 tests)

```
tests/Eod.Shared.Tests/Resilience/
├── CircuitBreakerTests.cs        # 30 tests
│   ├── Initial state tests
│   ├── Success execution tests
│   ├── Failure and state transition tests
│   ├── Half-open state tests
│   ├── Manual control tests
│   ├── Event notification tests
│   ├── Exception filtering tests
│   ├── Sliding window tests
│   ├── Thread safety tests
│   └── Metrics tests
├── CircuitBreakerFactoryTests.cs # 11 tests
│   ├── GetOrCreate tests
│   ├── Get tests
│   ├── GetAll tests
│   └── ResetAll tests
└── CircuitBreakerOptionsTests.cs # 4 tests
    ├── Default options tests
    └── Preset configuration tests
```

#### Benefits Achieved

```
┌─────────────────────────────────────────────────────────────────────┐
│                    BENEFITS                                         │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  WITHOUT Circuit Breaker:                                          │
│  ├── Service A calls failing Service B                             │
│  ├── Each call waits for timeout (30 seconds)                      │
│  ├── Thread pool exhausted                                         │
│  ├── Service A becomes unresponsive                                │
│  ├── Services C, D, E that depend on A also fail                   │
│  └── COMPLETE SYSTEM OUTAGE                                        │
│                                                                     │
│  WITH Circuit Breaker:                                             │
│  ├── Service A calls failing Service B                             │
│  ├── After 5 failures, circuit OPENS                               │
│  ├── Subsequent calls fail IMMEDIATELY (no timeout wait)           │
│  ├── Service A stays responsive (returns cached/default)           │
│  ├── After 30 seconds, circuit tests recovery                      │
│  └── GRACEFUL DEGRADATION                                          │
│                                                                     │
│  QUANTIFIED IMPACT:                                                │
│  ├── Response time under failure: 30,000ms → 1ms (30,000x faster) │
│  ├── Thread pool usage: 100% → Normal                              │
│  ├── Cascade failure risk: High → Eliminated                       │
│  └── Recovery time: Manual → Automatic                             │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### Enhancement 2: Dead Letter Queue (DLQ)

```
┌─────────────────────────────────────────────────────────────────────┐
│                    DEAD LETTER QUEUE                                │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  PROBLEM: Malformed trade message → Service throws → Message       │
│           retried infinitely → Consumer stuck                      │
│                                                                     │
│  SOLUTION: Route failed messages to DLQ after N retries            │
│                                                                     │
│  ```csharp                                                         │
│  async Task ProcessWithDlqAsync(ConsumeResult<string, byte[]> msg) │
│  {                                                                 │
│      var retryCount = GetRetryCount(msg.Headers);                  │
│      try                                                           │
│      {                                                             │
│          await ProcessTradeAsync(msg.Value);                       │
│      }                                                             │
│      catch (Exception ex) when (retryCount < 3)                    │
│      {                                                             │
│          await PublishWithRetryAsync(msg, retryCount + 1);         │
│      }                                                             │
│      catch (Exception ex)                                          │
│      {                                                             │
│          await PublishToDlqAsync(msg, ex);                         │
│          _metrics.IncrementDlq(msg.Topic);                         │
│      }                                                             │
│  }                                                                 │
│  ```                                                               │
│                                                                     │
│  MONITORING: Alert when DLQ depth > 0                              │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### Enhancement 3: Observability (OpenTelemetry)

```
┌─────────────────────────────────────────────────────────────────────┐
│                    OBSERVABILITY STACK                              │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  THREE PILLARS:                                                    │
│  ├── Metrics: Prometheus + Grafana                                 │
│  ├── Traces: Jaeger (distributed tracing)                          │
│  └── Logs: Seq or Elasticsearch                                    │
│                                                                     │
│  KEY METRICS TO TRACK:                                             │
│  ├── eod_ingestion_messages_per_second                             │
│  ├── eod_kafka_consumer_lag{group="flash-pnl"}                     │
│  ├── eod_pnl_update_latency_ms_p99                                 │
│  ├── eod_regulatory_batch_insert_duration_ms                       │
│  └── eod_redis_publish_errors_total                                │
│                                                                     │
│  DISTRIBUTED TRACE EXAMPLE:                                        │
│  ┌────────────────────────────────────────────────────────────┐    │
│  │ Trace ID: abc123                                           │    │
│  │ ├── Span: ingestion.receive_fix (2ms)                      │    │
│  │ │   └── Span: ingestion.publish_kafka (1ms)                │    │
│  │ ├── Span: flash_pnl.consume (5ms)                          │    │
│  │ │   ├── Span: flash_pnl.update_position (0.1ms)            │    │
│  │ │   └── Span: flash_pnl.publish_redis (2ms)                │    │
│  │ └── Span: regulatory.consume (150ms)                       │    │
│  │     ├── Span: regulatory.enrich (100ms)                    │    │
│  │     └── Span: regulatory.bulk_insert (50ms)                │    │
│  └────────────────────────────────────────────────────────────┘    │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### Enhancement 4: Schema Registry

```
┌─────────────────────────────────────────────────────────────────────┐
│                    SCHEMA REGISTRY                                  │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  PROBLEM: Producer changes Protobuf schema → Consumer crashes      │
│           because it expects old schema                            │
│                                                                     │
│  SOLUTION: Confluent Schema Registry                               │
│                                                                     │
│  WORKFLOW:                                                         │
│  1. Producer registers schema before publishing                    │
│  2. Schema Registry validates compatibility                        │
│  3. Consumer fetches schema by ID from message header              │
│  4. Backward/Forward compatible evolution enforced                 │
│                                                                     │
│  ```yaml                                                           │
│  # docker-compose.yml addition                                     │
│  schema-registry:                                                  │
│    image: confluentinc/cp-schema-registry:7.5.0                    │
│    depends_on:                                                     │
│      - kafka                                                       │
│    environment:                                                    │
│      SCHEMA_REGISTRY_HOST_NAME: schema-registry                    │
│      SCHEMA_REGISTRY_KAFKASTORE_BOOTSTRAP_SERVERS: kafka:29092     │
│    ports:                                                          │
│      - "8081:8081"                                                 │
│  ```                                                               │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### Enhancement 5: Exactly-Once Semantics

```
┌─────────────────────────────────────────────────────────────────────┐
│                    EXACTLY-ONCE PROCESSING                          │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  PROBLEM: Consumer crashes after processing but before commit      │
│           → Message reprocessed → Duplicate trade in database      │
│                                                                     │
│  SCENARIOS:                                                        │
│  ├── At-most-once: Commit before processing (risk: data loss)     │
│  ├── At-least-once: Commit after processing (risk: duplicates)    │
│  └── Exactly-once: Transactional processing (complex but correct)  │
│                                                                     │
│  SOLUTION FOR REGULATORY SERVICE:                                  │
│                                                                     │
│  ```csharp                                                         │
│  // Idempotent insert with MERGE                                   │
│  MERGE INTO dbo.Trades AS target                                   │
│  USING (SELECT @ExecId AS ExecId) AS source                        │
│  ON target.ExecId = source.ExecId                                  │
│  WHEN NOT MATCHED THEN                                             │
│      INSERT (ExecId, Symbol, ...) VALUES (@ExecId, @Symbol, ...);  │
│  ```                                                               │
│                                                                     │
│  ExecId is unique from exchange → Safe to retry                    │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### Enhancement 6: Backpressure Handling

```
┌─────────────────────────────────────────────────────────────────────┐
│                    BACKPRESSURE STRATEGY                            │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  PROBLEM: Kafka producer faster than consumer → Memory exhaustion  │
│                                                                     │
│  SOLUTION: Bounded channels + Async enumeration                    │
│                                                                     │
│  ```csharp                                                         │
│  // Bounded channel prevents memory explosion                      │
│  var channel = Channel.CreateBounded<Trade>(new BoundedChannelOptions(10000)│
│  {                                                                 │
│      FullMode = BoundedChannelFullMode.Wait,  // Block producer    │
│      SingleReader = false,                                         │
│      SingleWriter = false                                          │
│  });                                                               │
│                                                                     │
│  // Producer (Kafka consumer → Channel)                            │
│  await foreach (var msg in kafkaConsumer.ConsumeAsync())           │
│  {                                                                 │
│      await channel.Writer.WriteAsync(msg);  // Blocks if full     │
│  }                                                                 │
│                                                                     │
│  // Consumer (Channel → Processing)                                │
│  await foreach (var trade in channel.Reader.ReadAllAsync())        │
│  {                                                                 │
│      await ProcessTradeAsync(trade);                               │
│  }                                                                 │
│  ```                                                               │
│                                                                     │
│  MONITORING: Alert when channel.Reader.Count > 8000 (80% full)     │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 10. Implementation Roadmap

### Phase 1: Infrastructure Foundation (Week 1)

```
┌─────────────────────────────────────────────────────────────────────┐
│                       PHASE 1 TASKS                                 │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  □ Set up Docker Compose with all infrastructure services          │
│    ├── Kafka + Zookeeper                                           │
│    ├── Redis                                                       │
│    ├── SQL Server                                                  │
│    └── MinIO                                                       │
│                                                                     │
│  □ Create Protobuf schemas                                         │
│    └── trade_envelope.proto                                        │
│                                                                     │
│  □ Create SQL Server schema                                        │
│    ├── Trades table (with temporal)                                │
│    ├── Traders reference table                                     │
│    └── Strategies reference table                                  │
│                                                                     │
│  □ Create Kafka topics                                             │
│    ├── trades.raw (12 partitions)                                  │
│    └── trades.dlq (3 partitions)                                   │
│                                                                     │
│  DELIVERABLE: docker compose up brings all infra online            │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### Phase 2: Scenario A - Ingestion Service (Week 2)

```
┌─────────────────────────────────────────────────────────────────────┐
│                       PHASE 2 TASKS                                 │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  □ Create Eod.Ingestion .NET project                               │
│  □ Implement FIX protocol simulator (for testing)                  │
│  □ Implement Kafka producer with Protobuf serialization            │
│  □ Implement S3/MinIO archiver (async background)                  │
│  □ Add health check endpoint                                       │
│  □ Write Dockerfile                                                │
│  □ Integration test: FIX → Kafka → Verify message                  │
│                                                                     │
│  DELIVERABLE: Can ingest 10K messages/sec to Kafka                 │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### Phase 3: Scenario B - Flash P&L Engine (Week 3)

```
┌─────────────────────────────────────────────────────────────────────┐
│                       PHASE 3 TASKS                                 │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  □ Create Eod.FlashPnl .NET project                                │
│  □ Implement Kafka consumer with consumer group                    │
│  □ Implement in-memory position aggregator                         │
│  □ Implement waterfall mark pricing logic                          │
│  □ Implement Redis publisher (HSET + Pub/Sub)                      │
│  □ Add Polly circuit breaker for Redis                             │
│  □ Integration test: Kafka → Service → Redis → Verify P&L          │
│                                                                     │
│  DELIVERABLE: P&L updates in Redis within 100ms of trade           │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### Phase 4: Scenario C - Regulatory Reporter (Week 4)

```
┌─────────────────────────────────────────────────────────────────────┐
│                       PHASE 4 TASKS                                 │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  □ Create Eod.Regulatory .NET project                              │
│  □ Implement Kafka consumer (separate consumer group)              │
│  □ Implement reference data enrichment service                     │
│  □ Implement SqlBulkCopy batch inserter                            │
│  □ Implement DLQ for failed enrichments                            │
│  □ Integration test: Kafka → Service → SQL Server → Verify data    │
│                                                                     │
│  DELIVERABLE: All trades persisted to SQL with full audit trail    │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### Phase 5: Observability & Hardening (Week 5)

```
┌─────────────────────────────────────────────────────────────────────┐
│                       PHASE 5 TASKS                                 │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  □ Add OpenTelemetry to all services                               │
│  □ Deploy Prometheus + Grafana                                     │
│  □ Create EOD dashboard (consumer lag, latency, throughput)        │
│  □ Add Jaeger for distributed tracing                              │
│  □ Implement health checks for all services                        │
│  □ Load test: Simulate 100x burst                                  │
│                                                                     │
│  DELIVERABLE: Full visibility into system behavior under load      │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### Phase 6: Scaling & Production Readiness (Week 6)

```
┌─────────────────────────────────────────────────────────────────────┐
│                       PHASE 6 TASKS                                 │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  □ Test horizontal scaling (docker compose --scale)                │
│  □ Document scaling procedures                                     │
│  □ Create burst mode compose override                              │
│  □ Test failure scenarios (kill services, verify recovery)         │
│  □ Performance baseline documentation                              │
│  □ Runbook for EOD operations                                      │
│                                                                     │
│  DELIVERABLE: Production-ready system with operational docs        │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Appendix A: Quick Start Commands

```bash
# Clone and start infrastructure
git clone <repo>
cd eod-burst

# Start all infrastructure
docker compose up -d

# Wait for health checks
docker compose ps

# View Kafka topics
docker compose exec kafka kafka-topics --bootstrap-server localhost:9092 --list

# View Redis state
docker compose exec redis redis-cli HGETALL positions:TRADER001

# View SQL Server data
docker compose exec sqlserver /opt/mssql-tools/bin/sqlcmd \
  -S localhost -U sa -P "YourStrong@Passw0rd" \
  -Q "SELECT TOP 10 * FROM EodTrades.dbo.Trades"

# Scale for burst
docker compose up -d --scale flash-pnl=5 --scale regulatory=3

# View logs
docker compose logs -f flash-pnl

# Shutdown
docker compose down -v
```

---

## Appendix B: AWS Migration Path

When moving to AWS, the components map as follows:

| Local Component | AWS Equivalent | Notes |
|-----------------|----------------|-------|
| Kafka | Amazon MSK | Managed Kafka, same client code |
| Redis | Amazon ElastiCache | Redis-compatible |
| SQL Server | Amazon RDS for SQL Server | Same connection string |
| MinIO | Amazon S3 | S3-compatible API |
| Docker Compose | ECS or EKS | Container orchestration |
| Prometheus | Amazon CloudWatch | Metrics |
| Jaeger | AWS X-Ray | Distributed tracing |

---

## Appendix C: Glossary

| Term | Definition |
|------|------------|
| **CQRS** | Command Query Responsibility Segregation - separate read/write paths |
| **EOD** | End of Day - market close processing window |
| **Flash P&L** | Quick, approximate profit/loss calculation |
| **FIX** | Financial Information eXchange protocol |
| **LTP** | Last Traded Price |
| **CAT** | Consolidated Audit Trail (SEC regulation) |
| **TRACE** | Trade Reporting And Compliance Engine (FINRA) |
| **MOC** | Market On Close order |
| **LOC** | Limit On Close order |
| **DLQ** | Dead Letter Queue - failed message storage |

---

*Document Version: 1.0*  
*Last Updated: 2026-01-21*
