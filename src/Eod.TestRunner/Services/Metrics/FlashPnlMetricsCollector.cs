using Eod.TestRunner.Abstractions;
using Eod.TestRunner.Infrastructure;
using Eod.TestRunner.Models.Metrics;
using Microsoft.Extensions.Logging;

namespace Eod.TestRunner.Services.Metrics;

/// <summary>
/// Collects metrics from the Flash P&L service.
/// Follows SRP - only responsible for collecting Flash P&L metrics.
/// </summary>
public sealed class FlashPnlMetricsCollector : IMetricsCollector<FlashPnlMetrics>, IHealthCheckable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FlashPnlMetricsCollector> _logger;
    private const string BaseUrl = "http://flash-pnl:8081";
    
    public FlashPnlMetricsCollector(
        IHttpClientFactory httpClientFactory,
        ILogger<FlashPnlMetricsCollector> logger)
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
            _logger.LogDebug(ex, "Health check failed for Flash P&L service");
            return false;
        }
    }
    
    public async Task<FlashPnlMetrics> CollectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var isHealthy = await IsHealthyAsync(cancellationToken);
            
            if (!isHealthy)
            {
                return new FlashPnlMetrics { Status = "down" };
            }
            
            var metricsContent = await _httpClient.GetStringAsync(
                $"{BaseUrl}/metrics/prometheus", cancellationToken);
            
            var tradesProcessed = PrometheusMetricsParser.SumMetric(
                metricsContent, "eod_trades_processed_trades_total");
            
            var positionsInRedis = PrometheusMetricsParser.SumMetric(
                metricsContent, "eod_traders_count_traders");
            
            return new FlashPnlMetrics
            {
                TradesProcessed = tradesProcessed,
                PositionsInRedis = positionsInRedis,
                Status = "up"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to collect Flash P&L metrics");
            return new FlashPnlMetrics { Status = "down" };
        }
    }
}
