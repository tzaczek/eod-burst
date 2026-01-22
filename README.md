<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet" alt=".NET 8" />
  <img src="https://img.shields.io/badge/Kafka-3.6-231F20?style=for-the-badge&logo=apachekafka" alt="Kafka" />
  <img src="https://img.shields.io/badge/Redis-7-DC382D?style=for-the-badge&logo=redis" alt="Redis" />
  <img src="https://img.shields.io/badge/Docker-Compose-2496ED?style=for-the-badge&logo=docker" alt="Docker" />
  <img src="https://img.shields.io/badge/React-18-61DAFB?style=for-the-badge&logo=react" alt="React" />
</p>

<h1 align="center">⚡ EOD Burst System</h1>

<p align="center">
  <strong>High-performance distributed trade processing platform</strong><br>
  Designed to handle the extreme 100x load spike at market close (4:00 PM EST)
</p>

<p align="center">
  <a href="https://github.com/tzaczek/eod-burst">
    <img src="https://img.shields.io/badge/⭐_Star_&_Clone-GitHub-181717?style=for-the-badge&logo=github" alt="GitHub" />
  </a>
  <a href="https://github.com/tzaczek/eod-burst/stargazers">
    <img src="https://img.shields.io/github/stars/tzaczek/eod-burst?style=for-the-badge&color=yellow" alt="Stars" />
  </a>
  <a href="https://github.com/tzaczek/eod-burst/fork">
    <img src="https://img.shields.io/github/forks/tzaczek/eod-burst?style=for-the-badge&color=blue" alt="Forks" />
  </a>
</p>

<p align="center">
  🚀 <strong>Ready to build high-frequency trading systems?</strong><br>
  <a href="https://github.com/tzaczek/eod-burst"><b>Clone this repository</b></a> and have a production-ready CQRS architecture running in minutes!
</p>

<p align="center">
  <a href="#-key-capabilities">Capabilities</a> •
  <a href="#-quick-start">Quick Start</a> •
  <a href="#-architecture">Architecture</a> •
  <a href="#-technology-stack">Tech Stack</a> •
  <a href="#-observability">Observability</a> •
  <a href="#-testing">Testing</a>
</p>

---

## 📋 Table of Contents

- [Key Capabilities](#-key-capabilities)
- [The Problem We Solve](#-the-problem-we-solve)
- [Quick Start](#-quick-start)
- [Architecture](#-architecture)
  - [System Overview](#system-overview)
  - [Y-Split Architecture (CQRS)](#y-split-architecture-cqrs)
  - [Data Flow](#data-flow)
- [Technology Stack](#-technology-stack)
- [Application Components](#-application-components)
- [Design Patterns](#-design-patterns)
- [Resilience Patterns](#-resilience-patterns)
- [Observability](#-observability)
- [Testing](#-testing)
- [Configuration & Deployment](#-configuration--deployment)
- [Project Structure](#-project-structure)
- [Development](#-development)
- [AWS Migration Path](#-aws-migration-path)
- [License](#-license)

---

## 🎯 Key Capabilities

| Capability | Specification |
|:-----------|:--------------|
| **Ingestion Throughput** | 10,000+ trades/second |
| **P&L Latency** | < 100ms end-to-end |
| **Burst Handling** | 10x normal volume |
| **Regulatory Accuracy** | 100% (audit-compliant) |
| **Observability** | Full distributed tracing |

---

## 🔥 The Problem We Solve

At **4:00:00 PM EST** (market close), trading volume spikes **100x** above daily average due to:

- 📈 **NYSE D-Orders** — Discretionary orders released at close
- 🔄 **Closing Crosses** — MOC/LOC orders executed
- 🤖 **Algorithmic Liquidations** — Automated position squaring
- 📊 **End-of-Day Squaring** — Traders closing positions

### The Fundamental Tension

The business needs two **contradictory capabilities** simultaneously:

```
┌─────────────────────────────────┐    ┌─────────────────────────────────┐
│       ⚡ FLASH P&L              │    │      📋 REGULATORY              │
│       (Speed Path)              │    │      (Truth Path)               │
├─────────────────────────────────┤    ├─────────────────────────────────┤
│  Latency: ~100ms                │    │  Latency: Hours acceptable      │
│  Accuracy: ~99%                 │    │  Accuracy: 100% mandatory       │
│  Use: Trader hedging            │    │  Use: SEC/FINRA compliance      │
│  Storage: In-Memory + Redis     │    │  Storage: SQL Server + S3       │
└─────────────────────────────────┘    └─────────────────────────────────┘
```

**Solution:** CQRS Pattern — Split the pipeline at the source.

---

## 🚀 Quick Start

### Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) 4.x+
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (for local development)
- PowerShell 7+ (Windows) or Bash (Linux/Mac)

### Start the System

```powershell
# Clone the repository
git clone https://github.com/tzaczek/eod-burst.git
cd eod-burst

# Set up credentials (first time only)
cp .env.example .env

# Start all services with full observability
.\scripts\start-test-runner.ps1 -Build

# Or use Docker Compose directly
docker compose up -d
```

### Access Points

| Service | URL | Description |
|:--------|:----|:------------|
| 🧪 **Test Dashboard** | [localhost:8083](http://localhost:8083) | **Interactive test UI & architecture visualization** |
| 🔄 Ingestion | [localhost:8080/health](http://localhost:8080/health) | FIX gateway + Kafka producer |
| ⚡ Flash P&L | [localhost:8081/health](http://localhost:8081/health) | Real-time P&L engine |
| 📋 Regulatory | [localhost:8082/health](http://localhost:8082/health) | Batch SQL writer |

### Observability Stack

| Service | URL | Credentials |
|:--------|:----|:------------|
| 📊 Grafana | [localhost:3000](http://localhost:3000) | admin / admin |
| 🔍 Jaeger | [localhost:16686](http://localhost:16686) | — |
| 📨 Kafka UI | [localhost:8090](http://localhost:8090) | — |
| 💾 Redis Commander | [localhost:8091](http://localhost:8091) | — |
| 📦 MinIO Console | [localhost:9001](http://localhost:9001) | minioadmin / minioadmin |

---

## 🏗 Architecture

### System Overview

```
                                    ┌─────────────────────────────────────────┐
                                    │         EXTERNAL SYSTEMS                │
                                    │  (Exchanges, Brokers, Dark Pools)       │
                                    └─────────────────┬───────────────────────┘
                                                      │ FIX Protocol
                                                      ▼
┌─────────────────────────────────────────────────────────────────────────────────────────┐
│                              INGESTION SERVICE (Eod.Ingestion)                          │
│  ┌────────────────────────────────────────────────────────────────────────────────────┐ │
│  │   FIX Message ──▶ Checksum ──▶ Protobuf ──▶ Kafka Producer                        │ │
│  │   Channel        Validation    Serializer                                          │ │
│  │                       │                                                            │ │
│  │                       └──▶ MinIO (S3) ◀── Immutable Archive                        │ │
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
│   🔥 FLASH P&L SERVICE                    │          │   ❄️  REGULATORY SERVICE                  │
│                                           │          │                                           │
│  • In-Memory Position Store               │          │  • Reference Data Enrichment              │
│  • Waterfall Mark Pricing                 │          │  • Batch Buffer (5000 rows)               │
│  • Circuit Breaker Protected              │          │  • SqlBulkCopy Insert                     │
│                                           │          │                                           │
│        ▼                                  │          │        ▼                                  │
│  ┌─────────────┐                          │          │  ┌─────────────┐                          │
│  │    REDIS    │ Positions + Pub/Sub      │          │  │ SQL SERVER  │ Audit Trail              │
│  └─────────────┘                          │          │  └─────────────┘                          │
│                                           │          │                                           │
│   Latency: < 100ms                        │          │   Latency: < 4 hours                      │
└───────────────────────────────────────────┘          └───────────────────────────────────────────┘
```

### Y-Split Architecture (CQRS)

The system implements a **Y-Split Architecture** where:

1. **Ingestion Layer** — Receives FIX messages, archives raw bytes, publishes to Kafka
2. **Hot Path (Flash P&L)** — Processes trades in-memory for real-time trader dashboards  
3. **Cold Path (Regulatory)** — Enriches and persists trades for compliance

### Data Flow

```
1. FIX Message Received (FixSimulatorService or external)
         ▼
2. Checksum Validated → Invalid? Drop with metric
         ▼
3. Raw bytes queued to S3Archive (async, circuit breaker)
         ▼
4. Minimal FIX parsing (symbol for partition key)
         ▼
5. Serialize to TradeEnvelope (Protobuf)
         ▼
6. Publish to Kafka (trades.raw, partitioned by symbol)
         │
         ├────────────────────────────────────┐
         │                                    │
         ▼                                    ▼
   [Flash P&L]                          [Regulatory]
   • Update in-memory position          • Enrich with ref data
   • Calculate P&L                      • Buffer trades
   • Publish to Redis                   • Bulk insert to SQL
```

---

## ⚙️ Technology Stack

| Layer | Technology | Purpose |
|:------|:-----------|:--------|
| **Runtime** | .NET 8 / C# 12 | High-performance async processing |
| **Messaging** | Apache Kafka (KRaft) | Durable event streaming with replay |
| **Cache** | Redis 7 | Position state + Pub/Sub (~0.1ms latency) |
| **Database** | SQL Server 2022 | ACID-compliant regulatory persistence |
| **Object Storage** | MinIO (S3) | Raw FIX message archive |
| **Serialization** | Protocol Buffers | Binary encoding (no float precision loss) |
| **Tracing** | OpenTelemetry + Jaeger | Distributed tracing |
| **Metrics** | Prometheus + Grafana | Time-series metrics & dashboards |
| **Schema** | Confluent Schema Registry | Schema versioning & validation |
| **Container** | Docker Compose | Orchestration & scaling |

### Why These Choices?

<details>
<summary><strong>Why Kafka over RabbitMQ?</strong></summary>

- **Durability**: Messages persisted to disk, survives broker restart
- **Replay**: Can re-read from any offset for debugging/recovery
- **Partitioning**: Parallel processing with ordering per partition
- **Consumer Groups**: Multiple services read same topic independently
- **Throughput**: 1M+ messages/sec per broker

</details>

<details>
<summary><strong>Why Redis over Memcached?</strong></summary>

- **Data Structures**: `HSET` perfect for positions
- **Pub/Sub**: Built-in for pushing updates to subscribers
- **Atomic Operations**: `HINCRBY` for thread-safe updates
- **Persistence**: AOF for durability

</details>

<details>
<summary><strong>Why SQL Server over PostgreSQL?</strong></summary>

- **SqlBulkCopy**: Native .NET bulk insert (10x faster than EF)
- **Temporal Tables**: Built-in audit history (regulatory requirement)
- **Entity Framework**: Excellent .NET integration

</details>

---

## 📦 Application Components

### Eod.Ingestion
**Purpose**: Receive, validate, archive, and publish trade messages

| Class | Responsibility |
|:------|:---------------|
| `IngestionService` | Validates FIX checksum, publishes to Kafka |
| `FixSimulatorService` | Generates realistic FIX messages for testing |
| `S3ArchiveService` | Async archive to MinIO with circuit breaker |
| `MessageChannel` | Bounded channel for backpressure handling |

### Eod.FlashPnl  
**Purpose**: Real-time P&L calculation for trader dashboards

| Class | Responsibility |
|:------|:---------------|
| `FlashPnlService` | Main consumer, updates positions, publishes P&L |
| `PositionStore` | Thread-safe in-memory aggregator (`ConcurrentDictionary`) |
| `PriceService` | Waterfall mark pricing with cache |
| `ResilientRedisService` | Circuit breaker wrapper for Redis |

### Eod.Regulatory
**Purpose**: Enrich trades and persist for regulatory compliance

| Class | Responsibility |
|:------|:---------------|
| `RegulatoryService` | Enriches trades, buffers, bulk inserts |
| `ReferenceDataService` | Cached lookup for traders, securities |
| `BulkInsertService` | High-performance SQL bulk copy |

### Eod.TestRunner
**Purpose**: Interactive testing dashboard with real-time metrics

- React frontend with SignalR real-time updates
- Multiple test types: Health, Throughput, Latency, E2E, Burst
- Architecture visualization page

---

## 🧩 Design Patterns

| Pattern | Implementation | Purpose |
|:--------|:---------------|:--------|
| **CQRS** | Hot Path / Cold Path split | Separate read-optimized from write-optimized |
| **Circuit Breaker** | `ICircuitBreaker`, `CircuitBreakerFactory` | Prevent cascade failures |
| **Dead Letter Queue** | `DeadLetterQueueService` | Handle unprocessable messages |
| **Repository** | `IScenarioRepository` | Abstract data access |
| **Factory** | `ICircuitBreakerFactory` | Create configured instances |
| **Observer** | `ITestObserver` | Notify clients of test progress |
| **Decorator** | `ResilientRedisService` | Add circuit breaker to Redis |
| **BackgroundService** | All processing services | Long-running async processing |

---

## 🛡️ Resilience Patterns

### Circuit Breaker State Machine

```
    ┌──────────────┐    Failure Threshold    ┌──────────────┐
    │              │        Exceeded         │              │
    │    CLOSED    │ ──────────────────────▶ │     OPEN     │
    │   (Normal)   │                         │  (Fail Fast) │
    └──────────────┘                         └──────┬───────┘
           ▲                                        │
           │                                        │ OpenDuration
           │                                        │   Expires
           │                                        ▼
           │       Success Threshold      ┌──────────────┐
           │         Reached              │              │
           └───────────────────────────── │  HALF-OPEN   │
                                          │  (Test Mode) │
                        Failure           └──────────────┘
                        ─────────────────────────┘
```

### Behavior Under Failure

| Component | Circuit Breaker | Fallback |
|:----------|:----------------|:---------|
| S3 Archive | Open after 5 failures | Drop messages (acceptable) |
| Redis Publish | Open after 5 failures | Skip publish, process locally |
| Redis Query | Open after 10 failures | Use local price cache |

---

## 📊 Observability

### Pre-configured Grafana Dashboards

| Dashboard | Description |
|:----------|:------------|
| EOD Overview | System health, throughput |
| EOD Services | Application metrics |
| EOD Kafka | Consumer lag, partitions |
| EOD Redis | Memory, operations |
| EOD DLQ | Dead letter queue monitoring |
| EOD Tracing | Distributed trace overview |

### Key Metrics

```csharp
// Counters
IncrementTradesIngested(symbol)
IncrementTradesProcessed(traderId, symbol)
IncrementDlqMessages(service, reason)

// Histograms  
RecordIngestionLatency(ms, symbol)
RecordPnlCalculationLatency(ms)
RecordBulkInsertLatency(ms, batchSize)

// Gauges
SetConsumerLag(lag)
SetPositionCount(count)
```

### Distributed Tracing

Every trade is traced from ingestion through processing:

```
Trade Flow Trace:
├── ingestion.process_trade (2ms)
│   ├── kafka.produce (1ms)
│   └── s3.archive (async)
├── pnl.calculate (5ms)
│   ├── position.update (0.1ms)
│   └── redis.publish (2ms)
└── regulatory.enrich (150ms)
    └── sql.bulk_insert (50ms)
```

---

## 🧪 Testing

### Test Dashboard

Access at **[http://localhost:8083](http://localhost:8083)**

Features:
- 📊 **Architecture Page** — Interactive system visualization
- 🧪 **Testing Page** — Execute test scenarios with real-time progress
- 📈 **Performance Charts** — Live throughput and latency metrics

### Test Scenarios

| Type | Description | Success Criteria |
|:-----|:------------|:-----------------|
| `HealthCheck` | Verifies all services | All respond |
| `Throughput` | Measures sustainable rate | 95%+ of target |
| `Latency` | Measures E2E P&L update | P95 < 100ms |
| `EndToEnd` | Verifies complete flow | 95%+ trades in SQL |
| `BurstMode` | Simulates EOD spike | 80%+ of burst rate |
| `DataIntegrity` | Verifies correctness | Zero mismatches |
| `DeadLetterQueue` | Tests error handling | Invalid msgs in DLQ |
| `SchemaRegistry` | Tests schema management | Registration + validation |

### Unit Tests

- **CircuitBreakerTests**: 30 tests covering state transitions
- **CircuitBreakerFactoryTests**: 11 tests for instance management
- **CircuitBreakerOptionsTests**: 4 tests for presets

```powershell
# Run all tests
dotnet test

# Run specific project
dotnet test tests/Eod.Shared.Tests
```

---

## ⚙️ Configuration & Deployment

### 🔐 Credentials Setup (Required)

Credentials are managed via environment variables stored in a `.env` file that is **not committed to git**.

```powershell
# 1. Copy the example environment file
cp .env.example .env

# 2. Edit .env and set your credentials (or use defaults for development)
```

**Required environment variables** (set in `.env`):

| Variable | Description | Default (Dev) |
|:---------|:------------|:--------------|
| `MSSQL_SA_PASSWORD` | SQL Server SA password | `YourStrong@Passw0rd` |
| `MINIO_ROOT_USER` | MinIO access key | `minioadmin` |
| `MINIO_ROOT_PASSWORD` | MinIO secret key | `minioadmin` |
| `GF_SECURITY_ADMIN_USER` | Grafana admin user | `admin` |
| `GF_SECURITY_ADMIN_PASSWORD` | Grafana admin password | `admin` |

> ⚠️ **Security Note**: Never commit `.env` files with real credentials. The `.env` file is already in `.gitignore`.

### Quick Start Commands

```powershell
# 1. Set up credentials (first time only)
cp .env.example .env

# 2. Start all services
docker compose up -d

# Scale for burst mode
docker compose up -d --scale flash-pnl=3 --scale regulatory=2

# View logs
docker compose logs -f flash-pnl

# Stop all
docker compose down
```

### Environment Variables

| Variable | Default | Description |
|:---------|:--------|:------------|
| `ASPNETCORE_ENVIRONMENT` | Docker | Configuration profile |
| `Kafka__BootstrapServers` | kafka:29092 | Kafka connection |
| `Redis__ConnectionString` | redis:6379 | Redis connection |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | http://otel-collector:4317 | Telemetry endpoint |

### Scaling Limits

| Service | Normal | Burst | Max (Kafka partitions) |
|:--------|:-------|:------|:-----------------------|
| Ingestion | 1 | 2 | N/A (producer) |
| Flash P&L | 1 | 6 | 12 |
| Regulatory | 1 | 4 | 12 |

---

## 📁 Project Structure

```
eod/
├── src/
│   ├── Eod.Shared/              # Shared library
│   │   ├── Configuration/       # Typed settings
│   │   ├── Data/               # Entity Framework
│   │   ├── Kafka/              # Producer, Consumer, DLQ
│   │   ├── Models/             # Position, Trade
│   │   ├── Protos/             # Protocol Buffers
│   │   ├── Redis/              # Redis abstraction
│   │   ├── Resilience/         # Circuit breaker (45 tests)
│   │   └── Telemetry/          # OpenTelemetry
│   │
│   ├── Eod.Ingestion/          # FIX ingestion service
│   ├── Eod.FlashPnl/           # Real-time P&L service
│   ├── Eod.Regulatory/         # Compliance service
│   └── Eod.TestRunner/         # Test dashboard
│       └── ClientApp/          # React frontend
│
├── tests/                      # Unit & integration tests
├── infrastructure/             # Grafana, Prometheus configs
├── scripts/                    # PowerShell utilities
│
├── docker-compose.yml          # Main infrastructure
├── docker-compose.burst.yml    # Burst mode overlay
└── docker-compose.observability.yml
```

---

## 💻 Development

### Build Locally

```powershell
# Restore and build
dotnet restore
dotnet build

# Run specific service
cd src/Eod.FlashPnl
dotnet run
```

### Frontend Development

```powershell
cd src/Eod.TestRunner/ClientApp
npm install
npm run dev
```

### Docker Build

```powershell
docker compose build
```

---

## ☁️ AWS Migration Path

| Local Component | AWS Equivalent |
|:----------------|:---------------|
| Kafka | Amazon MSK |
| Redis | Amazon ElastiCache |
| SQL Server | Amazon RDS |
| MinIO | Amazon S3 |
| Docker Compose | ECS / EKS |
| Prometheus | CloudWatch |
| Jaeger | AWS X-Ray |

---

## 📚 Glossary

| Term | Definition |
|:-----|:-----------|
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

## 📄 License

MIT License — see [LICENSE](LICENSE) for details.

---

<p align="center">
  <sub>Built with ❤️ for high-frequency trading systems</sub>
</p>
