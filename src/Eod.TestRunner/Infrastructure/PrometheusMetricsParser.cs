using System.Text.RegularExpressions;

namespace Eod.TestRunner.Infrastructure;

/// <summary>
/// Utility class for parsing Prometheus metrics format.
/// Follows Single Responsibility Principle - only handles metrics parsing.
/// </summary>
public static partial class PrometheusMetricsParser
{
    /// <summary>
    /// Sums all values for a metric with the given name, handling labels.
    /// </summary>
    /// <param name="metricsContent">Raw Prometheus metrics content</param>
    /// <param name="metricName">Name of the metric to sum</param>
    /// <returns>Sum of all matching metric values</returns>
    public static long SumMetric(string metricsContent, string metricName)
    {
        ArgumentNullException.ThrowIfNull(metricsContent);
        ArgumentException.ThrowIfNullOrWhiteSpace(metricName);
        
        var pattern = $@"{Regex.Escape(metricName)}(?:\{{[^}}]*\}})?\s+(\d+(?:\.\d+)?)";
        var matches = Regex.Matches(metricsContent, pattern);
        
        return matches.Sum(m => 
        {
            if (double.TryParse(m.Groups[1].Value, out var value))
                return (long)value;
            return 0L;
        });
    }
    
    /// <summary>
    /// Gets a single metric value.
    /// </summary>
    /// <param name="metricsContent">Raw Prometheus metrics content</param>
    /// <param name="metricName">Name of the metric</param>
    /// <returns>The metric value, or 0 if not found</returns>
    public static long GetMetricValue(string metricsContent, string metricName)
    {
        ArgumentNullException.ThrowIfNull(metricsContent);
        ArgumentException.ThrowIfNullOrWhiteSpace(metricName);
        
        var pattern = $@"{Regex.Escape(metricName)}(?:\{{[^}}]*\}})?\s+(\d+(?:\.\d+)?)";
        var match = Regex.Match(metricsContent, pattern);
        
        if (match.Success && double.TryParse(match.Groups[1].Value, out var value))
            return (long)value;
            
        return 0;
    }
    
    /// <summary>
    /// Gets all metric values with their labels.
    /// </summary>
    /// <param name="metricsContent">Raw Prometheus metrics content</param>
    /// <param name="metricName">Name of the metric</param>
    /// <returns>Dictionary of label combinations to values</returns>
    public static IReadOnlyDictionary<string, long> GetMetricValues(string metricsContent, string metricName)
    {
        ArgumentNullException.ThrowIfNull(metricsContent);
        ArgumentException.ThrowIfNullOrWhiteSpace(metricName);
        
        var result = new Dictionary<string, long>();
        var pattern = $@"{Regex.Escape(metricName)}(\{{[^}}]*\}})?\s+(\d+(?:\.\d+)?)";
        var matches = Regex.Matches(metricsContent, pattern);
        
        foreach (Match match in matches)
        {
            var labels = match.Groups[1].Success ? match.Groups[1].Value : "";
            if (double.TryParse(match.Groups[2].Value, out var value))
            {
                result[labels] = (long)value;
            }
        }
        
        return result;
    }
}
