using Eod.TestRunner.Abstractions;
using Eod.TestRunner.Infrastructure;
using Eod.TestRunner.Models.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eod.TestRunner.Services.Metrics;

/// <summary>
/// Collects metrics from Kafka via the Kafka Exporter.
/// Follows SRP - only responsible for collecting Kafka metrics.
/// </summary>
public sealed class KafkaMetricsCollector : IMetricsCollector<KafkaMetrics>, IHealthCheckable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<KafkaMetricsCollector> _logger;
    private readonly string _exporterUrl;
    
    public KafkaMetricsCollector(
        IHttpClientFactory httpClientFactory,
        IOptions<MetricsCollectorSettings> settings,
        ILogger<KafkaMetricsCollector> logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(5);
        _exporterUrl = settings.Value.KafkaExporterUrl;
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
            _logger.LogDebug(ex, "Health check failed for Kafka exporter at {Url}", _exporterUrl);
            return false;
        }
    }
    
    public async Task<KafkaMetrics> CollectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var metricsContent = await _httpClient.GetStringAsync(
                $"{_exporterUrl}/metrics", cancellationToken);
            
            var offsetValues = PrometheusMetricsParser.GetMetricValues(
                metricsContent, "kafka_topic_partition_current_offset");
            
            var messagesInTopic = offsetValues
                .Where(kv => kv.Key.Contains("trades.raw"))
                .Sum(kv => kv.Value);
            
            var lagValues = PrometheusMetricsParser.GetMetricValues(
                metricsContent, "kafka_consumergroup_lag");
            
            var consumerLag = lagValues
                .Where(kv => kv.Key.Contains("trades.raw"))
                .Sum(kv => kv.Value);
            
            return new KafkaMetrics
            {
                MessagesInTopic = messagesInTopic,
                ConsumerLag = consumerLag,
                Status = "up"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to collect Kafka metrics from {Url}", _exporterUrl);
            return new KafkaMetrics { Status = "down" };
        }
    }
}
