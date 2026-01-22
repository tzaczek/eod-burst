using Eod.TestRunner.Abstractions;
using Eod.TestRunner.Models;

namespace Eod.TestRunner.Services;

/// <summary>
/// Factory for creating test scenarios.
/// Follows Factory Pattern - encapsulates object creation logic.
/// Follows OCP - can be extended for new scenario types without modification.
/// </summary>
public sealed class ScenarioFactory : IScenarioFactory
{
    public TestScenario CreateExecutionScenario(TestScenario template, TestParameters? overrideParams = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        
        return new TestScenario
        {
            Id = GenerateExecutionId(template.Id),
            Name = template.Name,
            Description = template.Description,
            Type = template.Type,
            Parameters = MergeParameters(template.Parameters, overrideParams)
        };
    }
    
    public TestScenario CreateCustomScenario(string name, TestType type, TestParameters parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(parameters);
        
        return new TestScenario
        {
            Id = GenerateCustomId(),
            Name = name,
            Description = $"Custom {type} test",
            Type = type,
            Parameters = parameters
        };
    }
    
    private static string GenerateExecutionId(string templateId) 
        => $"{templateId}-{DateTime.UtcNow:yyyyMMddHHmmss}";
    
    private static string GenerateCustomId() 
        => $"custom-{DateTime.UtcNow:yyyyMMddHHmmss}";
    
    private static TestParameters MergeParameters(TestParameters baseParams, TestParameters? overrides)
    {
        if (overrides == null) return baseParams;

        return new TestParameters
        {
            TradeCount = overrides.TradeCount > 0 ? overrides.TradeCount : baseParams.TradeCount,
            TradesPerSecond = overrides.TradesPerSecond > 0 ? overrides.TradesPerSecond : baseParams.TradesPerSecond,
            BurstMultiplier = overrides.BurstMultiplier > 0 ? overrides.BurstMultiplier : baseParams.BurstMultiplier,
            BurstDurationSeconds = overrides.BurstDurationSeconds > 0 ? overrides.BurstDurationSeconds : baseParams.BurstDurationSeconds,
            Symbols = overrides.Symbols.Length > 0 ? overrides.Symbols : baseParams.Symbols,
            TraderIds = overrides.TraderIds.Length > 0 ? overrides.TraderIds : baseParams.TraderIds,
            TimeoutSeconds = overrides.TimeoutSeconds > 0 ? overrides.TimeoutSeconds : baseParams.TimeoutSeconds,
            WarmupSeconds = overrides.WarmupSeconds > 0 ? overrides.WarmupSeconds : baseParams.WarmupSeconds,
            ExpectedLatencyMs = overrides.ExpectedLatencyMs > 0 ? overrides.ExpectedLatencyMs : baseParams.ExpectedLatencyMs,
            AcceptableErrorRate = overrides.AcceptableErrorRate > 0 ? overrides.AcceptableErrorRate : baseParams.AcceptableErrorRate,
            IngestionUrl = overrides.IngestionUrl ?? baseParams.IngestionUrl,
            FlashPnlUrl = overrides.FlashPnlUrl ?? baseParams.FlashPnlUrl,
            RegulatoryUrl = overrides.RegulatoryUrl ?? baseParams.RegulatoryUrl
        };
    }
}
