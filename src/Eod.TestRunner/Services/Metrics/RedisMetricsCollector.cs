using Eod.TestRunner.Abstractions;
using Eod.TestRunner.Infrastructure;
using Eod.TestRunner.Models.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eod.TestRunner.Services.Metrics;

/// <summary>
/// Collects metrics from Redis via the Redis Exporter.
/// Follows SRP - only responsible for collecting Redis metrics.
/// </summary>
public sealed class RedisMetricsCollector : IMetricsCollector<RedisMetrics>, IHealthCheckable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RedisMetricsCollector> _logger;
    private readonly string _exporterUrl;
    
    public RedisMetricsCollector(
        IHttpClientFactory httpClientFactory,
        IOptions<MetricsCollectorSettings> settings,
        ILogger<RedisMetricsCollector> logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(5);
        _exporterUrl = settings.Value.RedisExporterUrl;
        _logger = logger;
    }
    
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_exporterUrl}/metrics", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Health check failed for Redis exporter at {Url}", _exporterUrl);
            return false;
        }
    }
    
    public async Task<RedisMetrics> CollectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var metricsContent = await _httpClient.GetStringAsync(
                $"{_exporterUrl}/metrics", cancellationToken);
            
            var connectedClients = (int)PrometheusMetricsParser.GetMetricValue(
                metricsContent, "redis_connected_clients");
            
            var keysValues = PrometheusMetricsParser.GetMetricValues(
                metricsContent, "redis_db_keys");
            
            var keysCount = keysValues
                .Where(kv => kv.Key.Contains("db0"))
                .Sum(kv => kv.Value);
            
            return new RedisMetrics
            {
                ConnectedClients = connectedClients,
                KeysCount = keysCount,
                Status = "up"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to collect Redis metrics from {Url}", _exporterUrl);
            return new RedisMetrics { Status = "down" };
        }
    }
}
