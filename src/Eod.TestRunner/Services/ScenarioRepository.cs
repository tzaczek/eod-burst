using Eod.TestRunner.Abstractions;
using Eod.TestRunner.Models;

namespace Eod.TestRunner.Services;

/// <summary>
/// Repository for managing predefined test scenarios.
/// Follows Repository Pattern - abstracts data access for scenarios.
/// Follows SRP - only responsible for scenario storage/retrieval.
/// </summary>
public sealed class ScenarioRepository : IScenarioRepository
{
    private readonly IReadOnlyList<TestScenario> _scenarios;
    private readonly Dictionary<string, TestScenario> _scenarioLookup;
    
    public ScenarioRepository()
    {
        _scenarios = CreatePredefinedScenarios();
        _scenarioLookup = _scenarios.ToDictionary(s => s.Id);
    }
    
    public IReadOnlyList<TestScenario> GetAll() => _scenarios;
    
    public TestScenario? GetById(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _scenarioLookup.GetValueOrDefault(id);
    }
    
    public bool Exists(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _scenarioLookup.ContainsKey(id);
    }
    
    private static List<TestScenario> CreatePredefinedScenarios() =>
    [
        new TestScenario
        {
            Id = "health-check",
            Name = "System Health Check",
            Description = "Verifies all services, Kafka, and Redis are healthy and reachable.",
            Type = TestType.HealthCheck,
            Parameters = new TestParameters { TimeoutSeconds = 30 }
        },
        new TestScenario
        {
            Id = "throughput-basic",
            Name = "Basic Throughput Test",
            Description = "Tests system throughput with moderate load (100 trades at 10/sec).",
            Type = TestType.Throughput,
            Parameters = new TestParameters
            {
                TradeCount = 100,
                TradesPerSecond = 10,
                TimeoutSeconds = 60
            }
        },
        new TestScenario
        {
            Id = "throughput-high",
            Name = "High Throughput Test",
            Description = "Tests system throughput with high load (1000 trades at 100/sec).",
            Type = TestType.Throughput,
            Parameters = new TestParameters
            {
                TradeCount = 1000,
                TradesPerSecond = 100,
                TimeoutSeconds = 120
            }
        },
        new TestScenario
        {
            Id = "latency-test",
            Name = "End-to-End Latency Test",
            Description = "Measures time from trade generation to position update in Redis.",
            Type = TestType.Latency,
            Parameters = new TestParameters
            {
                TradeCount = 50,
                ExpectedLatencyMs = 100,
                TimeoutSeconds = 120
            }
        },
        new TestScenario
        {
            Id = "e2e-small",
            Name = "End-to-End (Small)",
            Description = "Complete flow test with 50 trades through all services.",
            Type = TestType.EndToEnd,
            Parameters = new TestParameters
            {
                TradeCount = 50,
                Symbols = ["AAPL", "MSFT"],
                TraderIds = ["T001"],
                TimeoutSeconds = 120
            }
        },
        new TestScenario
        {
            Id = "e2e-medium",
            Name = "End-to-End (Medium)",
            Description = "Complete flow test with 500 trades through all services.",
            Type = TestType.EndToEnd,
            Parameters = new TestParameters
            {
                TradeCount = 500,
                Symbols = ["AAPL", "MSFT", "GOOGL", "AMZN"],
                TraderIds = ["T001", "T002", "T003"],
                TimeoutSeconds = 180
            }
        },
        new TestScenario
        {
            Id = "burst-5x",
            Name = "EOD Burst Simulation (5x)",
            Description = "Simulates 5x traffic spike typical during market close.",
            Type = TestType.BurstMode,
            Parameters = new TestParameters
            {
                TradesPerSecond = 20,
                BurstMultiplier = 5,
                BurstDurationSeconds = 30,
                WarmupSeconds = 10,
                TimeoutSeconds = 120
            }
        },
        new TestScenario
        {
            Id = "burst-10x",
            Name = "EOD Burst Simulation (10x)",
            Description = "Simulates 10x traffic spike - stress test scenario.",
            Type = TestType.BurstMode,
            Parameters = new TestParameters
            {
                TradesPerSecond = 20,
                BurstMultiplier = 10,
                BurstDurationSeconds = 60,
                WarmupSeconds = 10,
                TimeoutSeconds = 180
            }
        },
        new TestScenario
        {
            Id = "data-integrity",
            Name = "Data Integrity Verification",
            Description = "Verifies data consistency between generated trades and SQL storage.",
            Type = TestType.DataIntegrity,
            Parameters = new TestParameters
            {
                TradeCount = 100,
                Symbols = ["AAPL"],
                TraderIds = ["INTEGRITY-TEST"],
                TimeoutSeconds = 180
            }
        },
        new TestScenario
        {
            Id = "dlq-test",
            Name = "Dead Letter Queue Test",
            Description = "Verifies that invalid/malformed messages are properly routed to the DLQ topic and can be monitored.",
            Type = TestType.DeadLetterQueue,
            Parameters = new TestParameters
            {
                TradeCount = 20,
                Symbols = ["AAPL", "MSFT"],
                TraderIds = ["DLQ-TEST"],
                TimeoutSeconds = 120
            }
        },
        new TestScenario
        {
            Id = "schema-registry-test",
            Name = "Schema Registry Test",
            Description = "Verifies Schema Registry integration: schema registration, compatibility checks, and schema-validated message production/consumption.",
            Type = TestType.SchemaRegistry,
            Parameters = new TestParameters
            {
                TradeCount = 50,
                Symbols = ["AAPL", "MSFT", "GOOGL"],
                TraderIds = ["SCHEMA-TEST"],
                TimeoutSeconds = 120
            }
        }
    ];
}
