namespace Eod.TestRunner.Models;

/// <summary>
/// Represents a parameterized test scenario.
/// </summary>
public class TestScenario
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required TestType Type { get; set; }
    public required TestParameters Parameters { get; set; }
}

public enum TestType
{
    HealthCheck,
    Throughput,
    Latency,
    EndToEnd,
    BurstMode,
    DataIntegrity,
    DeadLetterQueue,
    SchemaRegistry
}

/// <summary>
/// Parameters for test execution.
/// </summary>
public class TestParameters
{
    // Trade generation parameters
    public int TradeCount { get; set; } = 100;
    public int TradesPerSecond { get; set; } = 10;
    public int BurstMultiplier { get; set; } = 10;
    public int BurstDurationSeconds { get; set; } = 10;
    
    // Symbol parameters
    public string[] Symbols { get; set; } = ["AAPL", "MSFT", "GOOGL"];
    public string[] TraderIds { get; set; } = ["T001", "T002"];
    
    // Timing parameters
    public int TimeoutSeconds { get; set; } = 60;
    public int WarmupSeconds { get; set; } = 5;
    
    // Validation parameters
    public int ExpectedLatencyMs { get; set; } = 100;
    public double AcceptableErrorRate { get; set; } = 0.01; // 1%
    
    // Service endpoints (for custom testing)
    public string? IngestionUrl { get; set; }
    public string? FlashPnlUrl { get; set; }
    public string? RegulatoryUrl { get; set; }
}

/// <summary>
/// Result of a test execution.
/// </summary>
public class TestResult
{
    public required string ScenarioId { get; set; }
    public required string ScenarioName { get; set; }
    public TestStatus Status { get; set; } = TestStatus.Pending;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public TimeSpan? Duration => CompletedAt.HasValue ? CompletedAt.Value - StartedAt : null;
    
    // Metrics
    public int TradesGenerated { get; set; }
    public int TradesProcessedByPnl { get; set; }
    public int TradesInsertedToSql { get; set; }
    public int MessagesSentToDlq { get; set; }
    public int DlqMessagesVerified { get; set; }
    public double AverageLatencyMs { get; set; }
    public double P95LatencyMs { get; set; }
    public double P99LatencyMs { get; set; }
    public double ThroughputPerSecond { get; set; }
    public int ErrorCount { get; set; }
    
    // Schema Registry metrics
    public int SchemasRegistered { get; set; }
    public int SchemaValidationsPassed { get; set; }
    public int SchemaValidationsFailed { get; set; }
    
    // Detailed results
    public List<TestStep> Steps { get; set; } = [];
    public List<string> Errors { get; set; } = [];
    public Dictionary<string, object> Metadata { get; set; } = [];
}

public enum TestStatus
{
    Pending,
    Running,
    Passed,
    Failed,
    Cancelled
}

/// <summary>
/// Individual step within a test.
/// </summary>
public class TestStep
{
    public required string Name { get; set; }
    public TestStatus Status { get; set; } = TestStatus.Pending;
    public string? Message { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public TimeSpan? Duration => CompletedAt.HasValue && StartedAt.HasValue 
        ? CompletedAt.Value - StartedAt.Value : null;
}

/// <summary>
/// Real-time progress update.
/// </summary>
public class TestProgress
{
    public required string ScenarioId { get; set; }
    public required string CurrentStep { get; set; }
    public int PercentComplete { get; set; }
    public int TradesGenerated { get; set; }
    public int TradesProcessed { get; set; }
    public double CurrentThroughput { get; set; }
    public string? Message { get; set; }
}
