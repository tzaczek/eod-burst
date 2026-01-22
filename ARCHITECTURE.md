# End of Day (EOD) Burst System - Architecture Documentation

## Document Information

| Field | Value |
|-------|-------|
| **Version** | 2.0.0 |
| **Last Updated** | 2026-01-22 |
| **Status** | Production-Ready |

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [What This Solution Does](#2-what-this-solution-does)
3. [System Architecture](#3-system-architecture)
4. [Application Architecture](#4-application-architecture)
5. [Application Components](#5-application-components)
6. [Shared Library Components](#6-shared-library-components)
7. [Infrastructure Components](#7-infrastructure-components)
8. [Observability Stack](#8-observability-stack)
9. [Data Flow](#9-data-flow)
10. [Resilience Patterns](#10-resilience-patterns)
11. [Testing Infrastructure](#11-testing-infrastructure)
12. [Configuration & Deployment](#12-configuration--deployment)

---

## 1. Executive Summary

The **EOD Burst System** is a high-performance, distributed trade processing platform designed to handle the extreme load spike that occurs at market close (4:00 PM EST). The system solves the fundamental tension between **Speed** (Flash P&L for traders) and **Truth** (Regulatory compliance for SEC/FINRA).

### Key Capabilities

| Capability | Specification |
|------------|---------------|
| **Ingestion Throughput** | 10,000+ trades/second |
| **P&L Latency** | < 100ms end-to-end |
| **Burst Handling** | 10x normal volume |
| **Regulatory Accuracy** | 100% (audit-compliant) |
| **Observability** | Full distributed tracing |

### Technology Stack

| Layer | Technology |
|-------|------------|
| **Runtime** | .NET 8 / C# 12 |
| **Messaging** | Apache Kafka |
| **Cache** | Redis 7 |
| **Database** | SQL Server 2022 |
| **Object Storage** | MinIO (S3-compatible) |
| **Serialization** | Protocol Buffers |
| **Tracing** | OpenTelemetry + Jaeger |
| **Metrics** | Prometheus + Grafana |
| **Schema Management** | Confluent Schema Registry |
| **Containerization** | Docker + Docker Compose |

---

## 2. What This Solution Does

### The Core Problem

At 4:00:00 PM (market close), trading volume spikes **100x** above daily average due to:
- NYSE D-Orders (discretionary orders released at close)
- Closing crosses (MOC/LOC orders)
- Algorithmic liquidations
- End-of-day position squaring

The business needs two contradictory capabilities simultaneously:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                     THE DIVERGENCE OF SPEED AND TRUTH                        │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌─────────────────────────────┐    ┌─────────────────────────────────┐    │
│  │       FLASH P&L             │    │      REGULATORY/BOOKS           │    │
│  │  (Speed Path)               │    │      (Truth Path)               │    │
│  ├─────────────────────────────┤    ├─────────────────────────────────┤    │
│  │  Latency: ~100ms            │    │  Latency: Hours acceptable      │    │
│  │  Accuracy: ~99%             │    │  Accuracy: 100% mandatory       │    │
│  │  Use: Trader hedging        │    │  Use: SEC/FINRA compliance      │    │
│  │  Storage: In-Memory + Redis │    │  Storage: SQL Server + S3       │    │
│  └─────────────────────────────┘    └─────────────────────────────────┘    │
│                                                                             │
│  SOLUTION: CQRS Pattern - Split the pipeline at the source                 │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Solution Overview

The system implements a **Y-Split Architecture** where:

1. **Ingestion Layer** receives FIX messages, archives raw bytes, and publishes to Kafka
2. **Hot Path (Flash P&L)** processes trades in-memory for real-time trader dashboards
3. **Cold Path (Regulatory)** enriches and persists trades for compliance

---

## 3. System Architecture

### High-Level Architecture Diagram

```
                                    ┌─────────────────────────────────────────┐
                                    │         EXTERNAL SYSTEMS                │
                                    │  (Exchanges, Brokers, Dark Pools)       │
                                    └─────────────────┬───────────────────────┘
                                                      │
                                                      │ FIX Protocol
                                                      ▼
┌─────────────────────────────────────────────────────────────────────────────────────────┐
│                              INGESTION SERVICE (Eod.Ingestion)                          │
│  ┌────────────────────────────────────────────────────────────────────────────────────┐ │
│  │   ┌─────────────┐    ┌─────────────┐    ┌─────────────┐    ┌─────────────┐        │ │
│  │   │ FIX Message │───▶│ Checksum    │───▶│ Protobuf    │───▶│ Kafka       │        │ │
│  │   │ Channel     │    │ Validation  │    │ Serializer  │    │ Producer    │        │ │
│  │   └─────────────┘    └─────────────┘    └──────┬──────┘    └─────────────┘        │ │
│  │                                                │                                   │ │
│  │                                                │ Async Archive (Circuit Breaker)   │ │
│  │                                                ▼                                   │ │
│  │                                         ┌─────────────┐                            │ │
│  │                                         │ MinIO (S3)  │ ◀── Immutable Archive     │ │
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
                              │  + Schema Registry (Protobuf validation)   │
                              │  + DLQ: trades.dlq                         │
                              └────────────────────────────────────────────┘
                                          │                    │
                    ┌─────────────────────┘                    └─────────────────────┐
                    │                                                                │
                    │ Consumer Group: flash-pnl                                      │ Consumer Group: regulatory
                    ▼                                                                ▼
┌───────────────────────────────────────────┐          ┌───────────────────────────────────────────┐
│   FLASH P&L SERVICE (Eod.FlashPnl)        │          │   REGULATORY SERVICE (Eod.Regulatory)     │
│                                           │          │                                           │
│  ┌─────────────────────────────────────┐  │          │  ┌─────────────────────────────────────┐  │
│  │   In-Memory Position Store          │  │          │  │   Reference Data Enrichment         │  │
│  │   ConcurrentDictionary<Key,Position>│  │          │  │   - Trader Lookup                   │  │
│  └─────────────────────────────────────┘  │          │  │   - Strategy Mapping                │  │
│              │                            │          │  │   - Security Master                 │  │
│              ▼                            │          │  └─────────────────────────────────────┘  │
│  ┌─────────────────────────────────────┐  │          │              │                            │
│  │   Waterfall Mark Pricing            │  │          │              ▼                            │
│  │   1. Official Close                 │  │          │  ┌─────────────────────────────────────┐  │
│  │   2. Last Traded Price              │  │          │  │   Batch Buffer (5000 rows)          │  │
│  │   3. Bid/Ask Mid                    │  │          │  └─────────────────────────────────────┘  │
│  └─────────────────────────────────────┘  │          │              │                            │
│              │                            │          │              ▼                            │
│              ▼ (Circuit Breaker)          │          │  ┌─────────────────────────────────────┐  │
│  ┌─────────────────────────────────────┐  │          │  │   SqlBulkCopy Insert                │  │
│  │   Resilient Redis Service           │  │          │  │   (Entity Framework Core)           │  │
│  │   - Position HSET                   │  │          │  └─────────────────────────────────────┘  │
│  │   - P&L Pub/Sub                     │  │          │              │                            │
│  └─────────────────────────────────────┘  │          │              ▼                            │
│              │                            │          │  ┌─────────────────────────────────────┐  │
│              ▼                            │          │  │         SQL SERVER                  │  │
│  ┌─────────────────────────────────────┐  │          │  │  - dbo.Trades table                 │  │
│  │           REDIS                     │  │          │  │  - Entity Framework migrations     │  │
│  └─────────────────────────────────────┘  │          │  └─────────────────────────────────────┘  │
│                                           │          │                                           │
│   Latency Target: < 100ms                 │          │   Latency Target: < 4 hours              │
│   Processing: In-memory, no I/O blocking  │          │   Processing: Batch-oriented             │
└───────────────────────────────────────────┘          └───────────────────────────────────────────┘
                    │                                                    │
                    │                                                    │
                    └──────────────────────┬─────────────────────────────┘
                                           │
                                           ▼
                    ┌───────────────────────────────────────────┐
                    │   DEAD LETTER QUEUE (trades.dlq)          │
                    │   - Deserialization errors                │
                    │   - Validation failures                   │
                    │   - Processing errors (after retries)     │
                    └───────────────────────────────────────────┘
```

### Network Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          eod-network (Docker bridge)                        │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  INFRASTRUCTURE TIER                                                        │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐           │
│  │   Kafka     │ │    Redis    │ │  SQL Server │ │    MinIO    │           │
│  │  (KRaft)    │ │   :6379     │ │    :1433    │ │ :9000/:9001 │           │
│  │ :9092/:9093 │ └─────────────┘ └─────────────┘ └─────────────┘           │
│  └─────────────┘                                                           │
│  ┌─────────────┐                                                           │
│  │   Schema    │                                                           │
│  │  Registry   │                                                           │
│  │   :8085     │                                                           │
│  └─────────────┘                                                           │
│                                                                             │
│  APPLICATION TIER                                                           │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐           │
│  │  Ingestion  │ │  Flash P&L  │ │ Regulatory  │ │ Test Runner │           │
│  │    :8080    │ │    :8081    │ │    :8082    │ │    :8083    │           │
│  └─────────────┘ └─────────────┘ └─────────────┘ └─────────────┘           │
│                                                                             │
│  OBSERVABILITY TIER                                                         │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐           │
│  │ Prometheus  │ │   Grafana   │ │   Jaeger    │ │    OTEL     │           │
│  │    :9090    │ │    :3000    │ │   :16686    │ │  Collector  │           │
│  └─────────────┘ └─────────────┘ └─────────────┘ │ :4316/:4319 │           │
│                                                   └─────────────┘           │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐                           │
│  │   Kafka     │ │   Redis     │ │  Kafka UI   │                           │
│  │  Exporter   │ │  Exporter   │ │    :8090    │                           │
│  │    :9308    │ │    :9121    │ └─────────────┘                           │
│  └─────────────┘ └─────────────┘                                           │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 4. Application Architecture

### Design Patterns Implemented

| Pattern | Implementation | Purpose |
|---------|----------------|---------|
| **CQRS** | Hot Path / Cold Path split | Separate read-optimized (P&L) from write-optimized (Regulatory) |
| **Circuit Breaker** | `ICircuitBreaker`, `CircuitBreakerFactory` | Prevent cascade failures when dependencies fail |
| **Dead Letter Queue** | `DeadLetterQueueService` | Handle unprocessable messages without blocking |
| **Repository** | `IScenarioRepository` | Abstract data access for test scenarios |
| **Factory** | `IScenarioFactory`, `ICircuitBreakerFactory` | Create configured instances |
| **Observer** | `ITestObserver` in TestRunner | Notify clients of test progress |
| **Decorator** | `ResilientRedisService` | Add circuit breaker to Redis operations |
| **Strategy** | `CircuitBreakerOptions` presets | Configure different resilience behaviors |
| **BackgroundService** | All processing services | Long-running async processing |

### Project Structure

```
eod/
├── src/
│   ├── Eod.Ingestion/          # FIX ingestion service
│   │   ├── Program.cs          # Service entry point
│   │   ├── Services/
│   │   │   ├── IngestionService.cs      # Main message processor
│   │   │   ├── FixSimulatorService.cs   # FIX message generator for testing
│   │   │   ├── S3ArchiveService.cs      # Async archive to MinIO
│   │   │   └── MessageChannel.cs        # Bounded channel for backpressure
│   │   └── Dockerfile
│   │
│   ├── Eod.FlashPnl/           # Real-time P&L service
│   │   ├── Program.cs
│   │   ├── Services/
│   │   │   ├── FlashPnlService.cs       # Main consumer and processor
│   │   │   ├── PositionStore.cs         # In-memory position aggregator
│   │   │   ├── PriceService.cs          # Waterfall pricing with cache
│   │   │   ├── PriceUpdateService.cs    # Simulated price feed
│   │   │   └── ResilientRedisService.cs # Circuit breaker wrapper
│   │   └── Dockerfile
│   │
│   ├── Eod.Regulatory/         # Compliance/persistence service
│   │   ├── Program.cs
│   │   ├── Services/
│   │   │   ├── RegulatoryService.cs     # Main consumer and processor
│   │   │   ├── ReferenceDataService.cs  # Trader/Security enrichment
│   │   │   └── BulkInsertService.cs     # SqlBulkCopy operations
│   │   └── Dockerfile
│   │
│   ├── Eod.TestRunner/         # Testing and monitoring dashboard
│   │   ├── Program.cs
│   │   ├── Abstractions/       # Interfaces (ISP, DIP)
│   │   ├── Controllers/        # REST API endpoints
│   │   ├── Hubs/               # SignalR for real-time updates
│   │   ├── Services/           # Test execution, metrics collection
│   │   ├── Models/             # Test scenarios, results
│   │   └── ClientApp/          # React frontend
│   │
│   └── Eod.Shared/             # Shared library
│       ├── Configuration/      # Typed settings classes
│       ├── Data/               # Entity Framework, migrations
│       ├── Health/             # Health check services
│       ├── Kafka/              # Producer, Consumer, DLQ services
│       ├── Models/             # Position, PositionKey, EnrichedTrade
│       ├── Protos/             # Protocol Buffer definitions
│       ├── Redis/              # Redis abstraction
│       ├── Resilience/         # Circuit breaker implementation
│       ├── Schema/             # Schema Registry integration
│       └── Telemetry/          # OpenTelemetry configuration
│
├── tests/
│   ├── Eod.FlashPnl.Tests/
│   ├── Eod.Shared.Tests/       # Circuit breaker tests (45 tests)
│   └── Eod.TestRunner.Tests/
│
├── config/                      # External configuration files
│   ├── grafana/                 # Dashboards and provisioning
│   ├── otel/                    # OpenTelemetry Collector config
│   └── prometheus/              # Prometheus scrape configuration
│
├── docker-compose.yml           # Main infrastructure
├── docker-compose.burst.yml     # Burst mode overlay
├── docker-compose.observability.yml
└── scripts/                     # Operational scripts
```

---

## 5. Application Components

### 5.1 Eod.Ingestion

**Purpose**: Receive, validate, archive, and publish trade messages.

**Key Classes**:

| Class | Responsibility |
|-------|----------------|
| `IngestionService` | Main message processor - validates FIX checksum, parses minimal fields, publishes to Kafka |
| `FixSimulatorService` | Generates realistic FIX messages for testing, supports burst mode |
| `S3ArchiveService` | Async archive to MinIO with circuit breaker protection |
| `MessageChannel` | Bounded channel for backpressure handling |

**Processing Flow**:
```
1. Receive raw FIX bytes via MessageChannel
2. Validate FIX checksum (fast, no parsing)
3. Fire-and-forget archive to MinIO (circuit breaker protected)
4. Parse minimal fields for Kafka partitioning (symbol extraction)
5. Serialize to Protobuf TradeEnvelope
6. Publish to Kafka (partitioned by symbol for ordering)
```

**Configuration** (`appsettings.json`):
```json
{
  "Kafka": {
    "BootstrapServers": "kafka:29092",
    "TradesTopic": "trades.raw",
    "DlqTopic": "trades.dlq"
  },
  "Minio": {
    "Endpoint": "minio:9000",
    "ArchiveBucket": "eod-archive",
    "BufferSize": 1000
  },
  "Simulator": {
    "Enabled": true,
    "BaseRatePerSecond": 100,
    "BurstMultiplier": 10
  }
}
```

---

### 5.2 Eod.FlashPnl

**Purpose**: Real-time P&L calculation for trader dashboards.

**Key Classes**:

| Class | Responsibility |
|-------|----------------|
| `FlashPnlService` | Main consumer - processes trades, updates positions, publishes P&L |
| `PositionStore` | Thread-safe in-memory position aggregator using `ConcurrentDictionary` |
| `PriceService` | Waterfall mark pricing with local cache and Redis fallback |
| `ResilientRedisService` | Decorator adding circuit breaker protection to Redis operations |
| `PriceUpdateService` | Simulates market data feed for testing |

**Processing Pattern**: "Process Locally, Publish Optimistically"
```
1. Consume trade from Kafka (Protobuf deserialization)
2. Validate required fields (TraderId, Symbol)
3. Update in-memory position (nanoseconds, no I/O) - ALWAYS succeeds
4. Update LTP from trade (circuit breaker protected, fire-and-forget)
5. Get mark price from local cache (NEVER blocks on I/O)
6. Calculate P&L snapshot
7. Publish to Redis (circuit breaker protected) - best effort
8. If any step fails after retries → send to DLQ
```

**Circuit Breaker Configuration**:
```csharp
// Publish operations - aggressive (P&L can be stale briefly)
_publishCircuitBreaker = factory.GetOrCreate("FlashPnl-Redis-Publish",
    new CircuitBreakerOptions {
        FailureThreshold = 5,
        OpenDuration = TimeSpan.FromSeconds(15),
        SuccessThresholdInHalfOpen = 2
    });

// Query operations - less aggressive (local cache fallback)
_queryCircuitBreaker = factory.GetOrCreate("FlashPnl-Redis-Query",
    new CircuitBreakerOptions {
        FailureThreshold = 10,
        OpenDuration = TimeSpan.FromSeconds(10),
        SuccessThresholdInHalfOpen = 1
    });
```

**Waterfall Mark Pricing**:
```
Priority 1: Official Close Price (from exchange feed)
Priority 2: Last Traded Price (from our trades)
Priority 3: Bid/Ask Midpoint (from market data)
Priority 4: Yesterday's Close (stale fallback)
```

---

### 5.3 Eod.Regulatory

**Purpose**: Enrich trades with reference data and persist for regulatory compliance.

**Key Classes**:

| Class | Responsibility |
|-------|----------------|
| `RegulatoryService` | Main consumer - enriches trades, buffers, bulk inserts |
| `ReferenceDataService` | Cached lookup for traders, strategies, securities |
| `BulkInsertService` | High-performance SQL bulk copy operations |
| `DatabaseMigrationService` | EF Core migrations on startup |

**Processing Flow**:
```
1. Consume trade from Kafka
2. Validate required fields (ExecId required)
3. Enrich with reference data (Trader name, MPID, CUSIP, etc.)
4. Buffer enriched trades (batch size: 5000)
5. Bulk insert when buffer full or 5 seconds elapsed
6. Commit Kafka offsets after successful insert
7. Failed messages → DLQ after 3 retries
```

**Entity Framework Integration**:
- Uses `EodDbContext` with SQL Server provider
- Automatic migrations via `DatabaseMigrationService`
- Retry policy: 5 retries with 30-second max delay
- Command timeout: 60 seconds for bulk operations

**Trade Entity** (`Eod.Shared.Data.Trade`):
```csharp
public sealed class Trade
{
    public long TradeId { get; set; }           // Auto-generated
    public required string ExecId { get; set; } // Unique from exchange
    public required string Symbol { get; set; }
    public required long Quantity { get; set; }
    public required decimal Price { get; set; } // decimal(18,8)
    public required string Side { get; set; }
    public required DateTime ExecTimestampUtc { get; set; }
    // ... enriched fields: TraderName, TraderMpid, Cusip, etc.
}
```

---

### 5.4 Eod.TestRunner

**Purpose**: Testing dashboard with real-time metrics and scenario execution.

**Key Features**:
- React frontend with SignalR real-time updates
- Multiple test types: Health, Throughput, Latency, E2E, Burst, DLQ, Schema Registry
- Metrics aggregation from all services
- Swagger API documentation

**Architecture**:
```
┌──────────────────────────────────────────────────────────────────┐
│                        Test Runner                                │
├──────────────────────────────────────────────────────────────────┤
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐  │
│  │  REST API       │  │  SignalR Hub    │  │  React Frontend │  │
│  │  (Controllers)  │  │  (TestHub)      │  │  (ClientApp)    │  │
│  └────────┬────────┘  └────────┬────────┘  └────────┬────────┘  │
│           │                    │                    │            │
│           └────────────────────┼────────────────────┘            │
│                                │                                 │
│  ┌─────────────────────────────┼─────────────────────────────┐  │
│  │              TestExecutionService                          │  │
│  │  - Scenario execution (8 test types)                       │  │
│  │  - Observer pattern for notifications                      │  │
│  │  - Progress tracking via SignalR                           │  │
│  └────────────────────────────────────────────────────────────┘  │
│                                                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │              Metrics Collectors (ISP)                      │   │
│  │  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐     │   │
│  │  │Ingestion │ │ Kafka    │ │Flash P&L │ │Regulatory│     │   │
│  │  │Metrics   │ │ Metrics  │ │ Metrics  │ │ Metrics  │     │   │
│  │  └──────────┘ └──────────┘ └──────────┘ └──────────┘     │   │
│  │  ┌──────────┐ ┌──────────┐                                │   │
│  │  │  Redis   │ │SQL Server│                                │   │
│  │  │ Metrics  │ │ Metrics  │                                │   │
│  │  └──────────┘ └──────────┘                                │   │
│  └──────────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────┘
```

**Test Types**:

| Type | Description |
|------|-------------|
| `HealthCheck` | Verifies all services are responding |
| `Throughput` | Measures sustainable message rate |
| `Latency` | Measures end-to-end P&L update time |
| `EndToEnd` | Verifies complete flow: Kafka → Redis → SQL |
| `BurstMode` | Simulates EOD burst (10x normal volume) |
| `DataIntegrity` | Verifies data correctness in SQL |
| `DeadLetterQueue` | Tests DLQ handling of invalid messages |
| `SchemaRegistry` | Tests schema registration and validation |

---

## 6. Shared Library Components

### 6.1 Protos (`trade_envelope.proto`)

Protocol Buffer definitions for all message types:

```protobuf
message TradeEnvelope {
    bytes raw_fix = 1;              // Original FIX for replay
    int64 receive_timestamp_ticks = 2;
    string gateway_id = 4;
    
    string exec_id = 10;            // Unique from exchange
    string symbol = 20;
    int64 quantity = 30;
    int64 price_mantissa = 31;      // Price * 10^8 (no floating point)
    Side side = 33;
    string trader_id = 40;
    // ... more fields
}

enum Side { SIDE_UNSPECIFIED = 0; SIDE_BUY = 1; SIDE_SELL = 2; ... }

message PriceUpdate { ... }
message PnlUpdate { ... }
```

---

### 6.2 Resilience (`Eod.Shared.Resilience`)

Custom Circuit Breaker implementation with 45 unit tests:

```
┌─────────────────────────────────────────────────────────────────────┐
│                    CIRCUIT BREAKER STATE MACHINE                    │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│              ┌─────────────────────────────────────────┐           │
│              │                                         │           │
│              ▼                                         │           │
│    ┌──────────────┐    Failure Threshold    ┌─────────┴────┐      │
│    │              │        Exceeded         │              │      │
│    │    CLOSED    │ ──────────────────────► │     OPEN     │      │
│    │ (Normal)     │                         │ (Fail Fast)  │      │
│    └──────────────┘                         └───────┬──────┘      │
│            ▲                                        │             │
│            │                                        │ OpenDuration│
│            │                                        │   Expires   │
│            │                                        ▼             │
│            │       Success Threshold      ┌──────────────┐        │
│            │         Reached              │              │        │
│            └───────────────────────────── │  HALF-OPEN   │        │
│                                           │ (Test Mode)  │        │
│              Failure                      └──────────────┘        │
│              Occurs                              │                │
│                 └────────────────────────────────┘                │
│                           Back to OPEN                            │
└───────────────────────────────────────────────────────────────────┘
```

**Key Classes**:

| Class | Purpose |
|-------|---------|
| `ICircuitBreaker` | Interface for DI and testing |
| `CircuitBreaker` | Thread-safe implementation with sliding window |
| `CircuitBreakerOptions` | Configuration with presets |
| `CircuitBreakerFactory` | Manages named circuit breaker instances |
| `CircuitBreakerOpenException` | Thrown when circuit is open |

**Configuration Presets**:
```csharp
CircuitBreakerOptions.HighAvailability  // 3 failures, 15s open
CircuitBreakerOptions.ExternalService   // 5 failures, 60s open
CircuitBreakerOptions.Storage           // 10 failures, 30s open
```

---

### 6.3 Kafka (`Eod.Shared.Kafka`)

| Class | Purpose |
|-------|---------|
| `KafkaProducerService` | Async/sync message production |
| `KafkaConsumerService` | Async enumeration with commit control |
| `DeadLetterQueueService` | Routes failed messages with error classification |
| `DeadLetterMessage` | Error context for DLQ messages |
| `DlqReason` | Enum: DeserializationError, ValidationError, ProcessingError, etc. |

**DLQ Classification**:
```csharp
DlqReason.DeserializationError  // Invalid Protobuf/JSON
DlqReason.ValidationError       // Missing required fields
DlqReason.ProcessingError       // Business logic failure
DlqReason.TimeoutError          // Downstream timeout
DlqReason.DownstreamError       // SQL/Redis failure
```

---

### 6.4 Schema Registry (`Eod.Shared.Schema`)

Integration with Confluent Schema Registry for Protobuf schema management:

| Class | Purpose |
|-------|---------|
| `SchemaRegistryService` | Register, validate, cache schemas |
| `SchemaValidatedProducerService` | Producer with schema validation |
| `SchemaValidatedConsumerService` | Consumer with schema validation |

**Features**:
- Automatic schema registration
- Backward compatibility checking
- In-memory schema caching
- Multiple naming strategies (TopicName, RecordName, TopicRecordName)

---

### 6.5 Telemetry (`Eod.Shared.Telemetry`)

OpenTelemetry integration for distributed tracing and metrics:

**`TelemetryExtensions.AddEodTelemetry()`**:
- Configures tracing with Jaeger export
- Configures metrics with Prometheus export
- Auto-instruments: ASP.NET Core, HttpClient, Redis, SQL Server
- Custom sources for Kafka operations

**`EodActivitySource`**:
```csharp
StartIngestion(execId, symbol)      // Trade ingestion span
StartPnlCalculation(traderId, symbol) // P&L calculation span
StartRedisPublish(channel)          // Redis operation span
StartBulkInsert(batchSize)          // SQL bulk insert span
StartKafkaProduce(topic, key)       // Kafka produce span
```

**`EodMetrics`**:
```csharp
// Counters
IncrementTradesIngested(symbol)
IncrementTradesProcessed(traderId, symbol)
IncrementTradesInserted(count)
IncrementDlqMessages(service, reason)

// Histograms
RecordIngestionLatency(ms, symbol)
RecordPnlCalculationLatency(ms)
RecordBulkInsertLatency(ms, batchSize)

// Gauges
SetConsumerLag(lag)
SetPositionCount(count)
```

---

## 7. Infrastructure Components

### 7.1 Apache Kafka (KRaft Mode)

**Image**: `confluentinc/cp-kafka:7.6.0`

**Purpose**: Durable, partitioned event log for trade messages.

**Why Kafka**:
- **Durability**: Messages persisted to disk, survives broker restart
- **Replay**: Can re-read from any offset for debugging/recovery
- **Partitioning**: Parallel processing with ordering per partition
- **Consumer Groups**: Multiple services read same topic independently
- **Throughput**: 1M+ messages/sec per broker

**Why NOT RabbitMQ**:
- Messages deleted after acknowledgment (no replay)
- Memory-based (risk of data loss)
- No native partitioning

**Why NOT AWS SQS**:
- 256KB message size limit
- No ordering guarantees (FIFO queues limited)
- Higher latency

**KRaft Mode (No Zookeeper)**:

This system uses **KRaft (Kafka Raft)** mode, which is Kafka's built-in consensus mechanism that eliminates the need for Apache Zookeeper. KRaft has been production-ready since Kafka 3.3+.

**Benefits of KRaft over Zookeeper**:
- **Simpler Architecture**: Single system to manage instead of two
- **Faster Startup**: No Zookeeper initialization delay
- **Lower Resource Usage**: ~50% less memory overhead
- **Better Scaling**: Controller handles metadata more efficiently
- **Improved Recovery**: Faster failover in cluster scenarios

**Configuration**:
```yaml
environment:
  # KRaft mode configuration
  KAFKA_NODE_ID: 1
  KAFKA_PROCESS_ROLES: broker,controller    # Combined mode
  KAFKA_CONTROLLER_QUORUM_VOTERS: 1@kafka:9093
  CLUSTER_ID: 'MkU3OEVBNTcwNTJENDM2Qk'      # Unique cluster ID
  
  # Topic defaults
  KAFKA_NUM_PARTITIONS: 12          # Matches max consumer scale
  KAFKA_LOG_RETENTION_HOURS: 168    # 7 days retention
  KAFKA_AUTO_CREATE_TOPICS_ENABLE: "true"
```

**Topics**:
- `trades.raw` - 12 partitions, partitioned by symbol
- `trades.dlq` - Dead letter queue

---

### 7.2 Schema Registry

**Image**: `confluentinc/cp-schema-registry:7.5.0`

**Purpose**: Centralized schema management for Protobuf messages.

**Why Schema Registry**:
- **Schema Evolution**: Backward/forward compatibility enforcement
- **Contract Validation**: Producers/consumers agree on schema
- **Documentation**: Central source of truth for message formats
- **Debugging**: Decode messages without code

**Configuration**:
```yaml
environment:
  SCHEMA_REGISTRY_SCHEMA_COMPATIBILITY_LEVEL: backward
```

**Port**: 8085 (mapped from internal 8081)

---

### 7.3 Redis

**Image**: `redis:7-alpine`

**Purpose**: Real-time position state and P&L pub/sub.

**Why Redis**:
- **Data Structures**: `HSET` perfect for positions (`HSET positions:T123 AAPL 500`)
- **Pub/Sub**: Built-in for pushing updates to subscribers
- **Atomic Operations**: `HINCRBY` for thread-safe updates
- **Sub-millisecond latency**: ~0.1ms for hash operations

**Why NOT Memcached**:
- No data structures (only key-value)
- No pub/sub
- No persistence

**Configuration**:
```yaml
command: >
  redis-server 
  --appendonly yes           # AOF persistence
  --maxmemory 512mb          # Memory limit
  --maxmemory-policy allkeys-lru  # Eviction policy
  --save 60 1000             # RDB snapshots
```

---

### 7.4 SQL Server

**Image**: `mcr.microsoft.com/mssql/server:2022-latest`

**Purpose**: Regulatory trade persistence with ACID compliance.

**Why SQL Server**:
- **SqlBulkCopy**: Native .NET bulk insert (10x faster than EF)
- **Entity Framework**: Code-first migrations, LINQ queries
- **Temporal Tables**: Built-in audit history (regulatory requirement)
- **Partitioning**: Table partitioning by date for efficient queries

**Why NOT PostgreSQL**:
- Less mature .NET bulk insert support
- No native temporal tables

**Why NOT MongoDB**:
- Cannot enforce foreign key relationships
- Regulatory data requires strict schema

**Configuration**:
```yaml
environment:
  MSSQL_SA_PASSWORD: "YourStrong@Passw0rd"
  MSSQL_PID: "Developer"
  MSSQL_MEMORY_LIMIT_MB: 2048
```

---

### 7.5 MinIO

**Image**: `minio/minio:latest`

**Purpose**: S3-compatible object storage for raw FIX message archive.

**Why MinIO**:
- **S3 API Compatible**: Same code works with AWS S3
- **Local Development**: No cloud dependency
- **Immutable Archive**: Raw bytes preserved for replay

**Use Case**:
```
Problem: Exchange updates FIX spec → Parser crashes → Trades lost
Solution: Archive raw bytes BEFORE parsing
Recovery: Download from S3, replay through fixed parser
```

**Ports**:
- 9000: S3 API
- 9001: Web Console

---

### 7.6 Kafka UI

**Image**: `provectuslabs/kafka-ui:latest`

**Purpose**: Web UI for Kafka administration and debugging.

**Features**:
- Topic browser with message viewer
- Consumer group monitoring
- Schema Registry integration
- Cluster health dashboard

**Port**: 8090

---

### 7.7 Redis Commander

**Image**: `rediscommander/redis-commander:latest`

**Purpose**: Web UI for Redis data browsing.

**Features**:
- Key browser
- Hash/List/Set visualization
- Command execution

**Port**: 8091

---

## 8. Observability Stack

### 8.1 Prometheus

**Image**: `prom/prometheus:latest`

**Purpose**: Time-series metrics storage and querying.

**Scrape Targets**:
```yaml
scrape_configs:
  - job_name: 'ingestion'
    metrics_path: '/metrics/prometheus'
    targets: ['ingestion:8080']
  - job_name: 'flash-pnl'
    targets: ['flash-pnl:8081']
  - job_name: 'regulatory'
    targets: ['regulatory:8082']
  - job_name: 'kafka'
    targets: ['kafka-exporter:9308']
  - job_name: 'redis'
    targets: ['redis-exporter:9121']
```

**Port**: 9090

---

### 8.2 Grafana

**Image**: `grafana/grafana:latest`

**Purpose**: Metrics visualization and dashboards.

**Pre-configured Dashboards**:
- EOD Overview (system health)
- EOD Services (application metrics)
- EOD Kafka (consumer lag, throughput)
- EOD Redis (memory, operations)
- EOD DLQ (dead letter queue monitoring)
- EOD Tracing (distributed trace overview)

**Port**: 3000 (admin/admin)

---

### 8.3 Jaeger

**Image**: `jaegertracing/all-in-one:latest`

**Purpose**: Distributed tracing visualization.

**Features**:
- Trace search and visualization
- Service dependency graph
- Latency analysis

**Ports**:
- 16686: UI
- 4317: OTLP gRPC
- 4318: OTLP HTTP

---

### 8.4 OpenTelemetry Collector

**Image**: `otel/opentelemetry-collector-contrib:latest`

**Purpose**: Central telemetry pipeline.

**Configuration** (`otel-collector-config.yaml`):
```yaml
receivers:
  otlp:
    protocols:
      grpc: { endpoint: 0.0.0.0:4317 }
      http: { endpoint: 0.0.0.0:4318 }

processors:
  batch: { timeout: 5s }
  memory_limiter: { limit_mib: 400 }

exporters:
  otlp/jaeger: { endpoint: jaeger:4317 }
  prometheus: { endpoint: "0.0.0.0:8889" }

pipelines:
  traces: [otlp] -> [batch] -> [jaeger]
  metrics: [otlp] -> [batch] -> [prometheus]
```

---

### 8.5 Kafka Exporter

**Image**: `danielqsj/kafka-exporter:latest`

**Purpose**: Export Kafka metrics to Prometheus.

**Key Metrics**:
- `kafka_consumergroup_lag` - Consumer lag per partition
- `kafka_topic_partition_current_offset`
- `kafka_brokers` - Broker count

**Port**: 9308

---

### 8.6 Redis Exporter

**Image**: `oliver006/redis_exporter:latest`

**Purpose**: Export Redis metrics to Prometheus.

**Key Metrics**:
- `redis_memory_used_bytes`
- `redis_commands_processed_total`
- `redis_connected_clients`

**Port**: 9121

---

## 9. Data Flow

### Normal Operation Flow

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                           TRADE DATA FLOW                                     │
├──────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  1. FIX Message Received (FixSimulatorService or external)                  │
│     ▼                                                                        │
│  2. Checksum Validated → Invalid? Drop with metric                          │
│     ▼                                                                        │
│  3. Raw bytes queued to S3Archive (async, circuit breaker)                  │
│     ▼                                                                        │
│  4. Minimal FIX parsing (symbol for partition key)                          │
│     ▼                                                                        │
│  5. Serialize to TradeEnvelope (Protobuf)                                   │
│     ▼                                                                        │
│  6. Publish to Kafka (trades.raw, partitioned by symbol)                    │
│     │                                                                        │
│     ├────────────────────────────────────────────────────────┐              │
│     │                                                        │              │
│     ▼ Consumer Group: flash-pnl                              ▼              │
│  7. Flash P&L consumes                              8. Regulatory consumes  │
│     │                                                        │              │
│     ▼                                                        ▼              │
│  9. Deserialize Protobuf                           10. Deserialize Protobuf │
│     │ └─ Error? → DLQ                                  │ └─ Error? → DLQ   │
│     ▼                                                        ▼              │
│ 11. Validate TraderId/Symbol                       12. Validate ExecId      │
│     │ └─ Invalid? → DLQ                                │ └─ Invalid? → DLQ │
│     ▼                                                        ▼              │
│ 13. Update in-memory position                      14. Enrich with ref data │
│     │ (ConcurrentDictionary)                            │ (cached lookups) │
│     ▼                                                        ▼              │
│ 15. Calculate P&L with mark price                  16. Buffer enriched trade│
│     │ (waterfall: Close→LTP→Mid)                        │ (5000 per batch) │
│     ▼                                                        ▼              │
│ 17. Publish to Redis                               18. SqlBulkCopy insert   │
│     │ (circuit breaker protected)                       │ (EF Core)        │
│     ▼                                                        ▼              │
│ 18. Trader Dashboard                               19. Regulatory Database  │
│                                                                              │
└──────────────────────────────────────────────────────────────────────────────┘
```

### Error Handling Flow

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                           ERROR HANDLING FLOW                                 │
├──────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  Message Processing Attempt                                                  │
│     ▼                                                                        │
│  ┌─────────────────────────┐                                                │
│  │ Deserialize Protobuf    │────── Exception ────┐                          │
│  └────────────┬────────────┘                     │                          │
│               ▼                                  ▼                          │
│  ┌─────────────────────────┐           ┌──────────────────┐                 │
│  │ Validate Required Fields│           │ DLQ: Deser Error │                 │
│  └────────────┬────────────┘           └──────────────────┘                 │
│               │                                                              │
│               ├─────── Missing TraderId/Symbol ──────┐                      │
│               │                                      ▼                      │
│               │                              ┌──────────────────┐           │
│               │                              │ DLQ: Validation  │           │
│               │                              └──────────────────┘           │
│               ▼                                                              │
│  ┌─────────────────────────┐                                                │
│  │ Process Trade           │                                                │
│  │ (Retry up to 3 times)   │                                                │
│  └────────────┬────────────┘                                                │
│               │                                                              │
│               ├─────── All retries failed ───────────┐                      │
│               │                                      ▼                      │
│               │                              ┌──────────────────┐           │
│               │                              │ DLQ: Processing  │           │
│               │                              │ (with retry cnt) │           │
│               │                              └──────────────────┘           │
│               ▼                                                              │
│  ┌─────────────────────────┐                                                │
│  │ Commit Kafka Offset     │                                                │
│  └─────────────────────────┘                                                │
│                                                                              │
└──────────────────────────────────────────────────────────────────────────────┘
```

---

## 10. Resilience Patterns

### Circuit Breaker Implementation

The system uses custom circuit breakers for:
- **S3 Archive**: Prevents ingestion blocking when MinIO is slow
- **Redis Publish**: Allows P&L processing to continue when Redis fails
- **Redis Query**: Falls back to local cache when Redis unavailable

**Behavior Under Failure**:

| Component | Circuit Breaker | Fallback |
|-----------|-----------------|----------|
| S3 Archive | Open after 5 failures | Drop messages (acceptable loss) |
| Redis Publish | Open after 5 failures | Skip publish, process locally |
| Redis Query | Open after 10 failures | Use local price cache |

### Dead Letter Queue

All services send unprocessable messages to `trades.dlq` with:
- Original message (base64 encoded)
- Error type classification
- Retry count
- Timestamp and metadata

### Backpressure Handling

```csharp
// Bounded channel prevents memory explosion
var channel = Channel.CreateBounded<RawFixMessage>(
    new BoundedChannelOptions(10000) {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true
    });
```

---

## 11. Testing Infrastructure

### Test Scenarios

| Scenario | Purpose | Success Criteria |
|----------|---------|------------------|
| Health Check | Verify service connectivity | All services respond |
| Throughput | Measure sustainable rate | 95%+ of target rate |
| Latency | Measure E2E P&L update | P95 < 100ms |
| End-to-End | Verify complete flow | 95%+ trades in SQL |
| Burst Mode | Simulate EOD spike | 80%+ of burst rate |
| Data Integrity | Verify data correctness | Zero mismatches |
| DLQ Test | Verify error handling | Invalid messages in DLQ |
| Schema Registry | Verify schema management | Registration + validation |

### Unit Tests

- **CircuitBreakerTests**: 30 tests covering state transitions
- **CircuitBreakerFactoryTests**: 11 tests for instance management
- **CircuitBreakerOptionsTests**: 4 tests for presets
- **ResilientRedisServiceTests**: Decorator behavior tests

---

## 12. Configuration & Deployment

### Quick Start

```bash
# Start all services
docker compose up -d

# Wait for health checks
docker compose ps

# Access dashboards
# - Test Runner: http://localhost:8083
# - Grafana: http://localhost:3000 (admin/admin)
# - Jaeger: http://localhost:16686
# - Kafka UI: http://localhost:8090
# - Redis Commander: http://localhost:8091
# - MinIO Console: http://localhost:9001 (minioadmin/minioadmin)

# Scale for burst
docker compose up -d --scale flash-pnl=3 --scale regulatory=2

# View logs
docker compose logs -f flash-pnl

# Stop
docker compose down
```

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `ASPNETCORE_ENVIRONMENT` | Docker | Configuration profile |
| `Kafka__BootstrapServers` | kafka:29092 | Kafka connection |
| `Redis__ConnectionString` | redis:6379 | Redis connection |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | http://otel-collector:4317 | Telemetry endpoint |

### Resource Allocation

```yaml
# docker-compose.yml
ingestion:
  deploy:
    resources:
      limits: { cpus: '1', memory: 512M }
      
flash-pnl:
  deploy:
    replicas: 1  # Scale to 3 for burst
    resources:
      limits: { cpus: '2', memory: 1G }

regulatory:
  deploy:
    replicas: 1  # Scale to 2 for burst
    resources:
      limits: { cpus: '2', memory: 2G }
```

### AWS Migration Path

| Local | AWS Equivalent |
|-------|----------------|
| Kafka | Amazon MSK |
| Redis | Amazon ElastiCache |
| SQL Server | Amazon RDS |
| MinIO | Amazon S3 |
| Docker Compose | ECS/EKS |
| Prometheus | CloudWatch |
| Jaeger | AWS X-Ray |

---

## Appendix: Glossary

| Term | Definition |
|------|------------|
| **CQRS** | Command Query Responsibility Segregation |
| **EOD** | End of Day |
| **Flash P&L** | Quick, approximate profit/loss |
| **FIX** | Financial Information eXchange protocol |
| **LTP** | Last Traded Price |
| **DLQ** | Dead Letter Queue |
| **CAT** | Consolidated Audit Trail (SEC) |
| **MPID** | Market Participant Identifier |
| **MOC/LOC** | Market/Limit On Close orders |

---

*Document Version: 2.0.0*  
*Last Updated: 2026-01-22*
