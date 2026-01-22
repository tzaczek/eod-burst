using Eod.TestRunner.Models.Metrics;

namespace Eod.TestRunner.Abstractions;

/// <summary>
/// Aggregates metrics from all system components.
/// Follows Single Responsibility Principle - only aggregates metrics.
/// </summary>
public interface ISystemMetricsAggregator
{
    /// <summary>
    /// Collects and aggregates metrics from all system components.
    /// </summary>
    Task<SystemMetrics> CollectAllMetricsAsync(CancellationToken cancellationToken = default);
}
