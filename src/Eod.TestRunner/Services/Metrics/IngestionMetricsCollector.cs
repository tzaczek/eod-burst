using Eod.Shared.Resilience;
using Eod.TestRunner.Abstractions;
using Eod.TestRunner.Infrastructure;
using Eod.TestRunner.Models.Metrics;
using Microsoft.Extensions.Logging;

namespace Eod.TestRunner.Services.Metrics;

/// <summary>
/// Collects metrics from the Ingestion service.
/// Follows SRP - only responsible for collecting ingestion metrics.
/// Uses Circuit Breaker pattern to prevent cascading failures.
/// </summary>
public sealed class IngestionMetricsCollector : IMetricsCollector<IngestionMetrics>, IHealthCheckable
{
    private readonly HttpClient _httpClient;
    private readonly ICircuitBreaker _circuitBreaker;
    private readonly ILogger<IngestionMetricsCollector> _logger;
    private const string BaseUrl = "http://ingestion:8080";
    
    public IngestionMetricsCollector(
        IHttpClientFactory httpClientFactory,
        ICircuitBreakerFactory circuitBreakerFactory,
        ILogger<IngestionMetricsCollector> logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _circuitBreaker = circuitBreakerFactory.GetOrCreate(
            "IngestionMetrics", 
            CircuitBreakerOptions.HighAvailability with { Name = "IngestionMetrics" });
        _logger = logger;
    }
    
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        // Don't use circuit breaker for health checks - they inform the circuit breaker
        try
        {
            var response = await _httpClient.GetAsync($"{BaseUrl}/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Health check failed for Ingestion service");
            return false;
        }
    }
    
    public async Task<IngestionMetrics> CollectAsync(CancellationToken cancellationToken = default)
    {
        // Fast fail if circuit is open
        if (_circuitBreaker.State == CircuitBreakerState.Open)
        {
            _logger.LogDebug("Circuit breaker open for Ingestion metrics, returning cached/default");
            return new IngestionMetrics { Status = "circuit-open" };
        }
        
        try
        {
            return await _circuitBreaker.ExecuteAsync(async ct =>
            {
                var isHealthy = await IsHealthyAsync(ct);
                
                if (!isHealthy)
                {
                    // Health check failure should trip the breaker
                    throw new HttpRequestException("Service unhealthy");
                }
                
                var metricsContent = await _httpClient.GetStringAsync(
                    $"{BaseUrl}/metrics/prometheus", ct);
                
                var tradesIngested = PrometheusMetricsParser.SumMetric(
                    metricsContent, "eod_trades_ingested_trades_total");
                
                return new IngestionMetrics
                {
                    TradesIngested = tradesIngested,
                    MessagesPerSecond = 0,
                    Status = "up"
                };
            }, cancellationToken);
        }
        catch (CircuitBreakerOpenException)
        {
            _logger.LogDebug("Circuit breaker tripped for Ingestion metrics");
            return new IngestionMetrics { Status = "circuit-open" };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to collect Ingestion metrics");
            return new IngestionMetrics { Status = "down" };
        }
    }
}
