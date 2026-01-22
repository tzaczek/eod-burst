namespace Eod.TestRunner.Abstractions;

/// <summary>
/// Base interface for collecting metrics from a specific component.
/// Follows Interface Segregation Principle - small, focused interface.
/// </summary>
public interface IMetricsCollector<TMetrics> where TMetrics : class
{
    /// <summary>
    /// Collects metrics from the component asynchronously.
    /// </summary>
    Task<TMetrics> CollectAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Marker interface for health-checkable components.
/// </summary>
public interface IHealthCheckable
{
    /// <summary>
    /// Checks if the component is healthy.
    /// </summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}
