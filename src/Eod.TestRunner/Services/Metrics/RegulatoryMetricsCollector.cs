using Eod.TestRunner.Abstractions;
using Eod.TestRunner.Infrastructure;
using Eod.TestRunner.Models.Metrics;
using Microsoft.Extensions.Logging;

namespace Eod.TestRunner.Services.Metrics;

/// <summary>
/// Collects metrics from the Regulatory service.
/// Follows SRP - only responsible for collecting Regulatory metrics.
/// </summary>
public sealed class RegulatoryMetricsCollector : IMetricsCollector<RegulatoryMetrics>, IHealthCheckable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RegulatoryMetricsCollector> _logger;
    private const string BaseUrl = "http://regulatory:8082";
    
    public RegulatoryMetricsCollector(
        IHttpClientFactory httpClientFactory,
        ILogger<RegulatoryMetricsCollector> logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _logger = logger;
    }
    
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{BaseUrl}/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Health check failed for Regulatory service");
            return false;
        }
    }
    
    public async Task<RegulatoryMetrics> CollectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var isHealthy = await IsHealthyAsync(cancellationToken);
            
            if (!isHealthy)
            {
                return new RegulatoryMetrics { Status = "down" };
            }
            
            var metricsContent = await _httpClient.GetStringAsync(
                $"{BaseUrl}/metrics/prometheus", cancellationToken);
            
            var tradesInserted = PrometheusMetricsParser.SumMetric(
                metricsContent, "eod_trades_inserted_trades_total");
            
            return new RegulatoryMetrics
            {
                TradesInserted = tradesInserted,
                BatchesPending = 0,
                Status = "up"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to collect Regulatory metrics");
            return new RegulatoryMetrics { Status = "down" };
        }
    }
}
