namespace Eod.TestRunner.Services;

public class HealthCheckService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HealthCheckService> _logger;

    public HealthCheckService(IHttpClientFactory httpClientFactory, ILogger<HealthCheckService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<bool> CheckServiceHealthAsync(string baseUrl, CancellationToken ct = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            
            var response = await client.GetAsync($"{baseUrl}/health", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Health check failed for {Url}", baseUrl);
            return false;
        }
    }

    public async Task<Dictionary<string, bool>> CheckAllServicesAsync(CancellationToken ct = default)
    {
        var services = new Dictionary<string, string>
        {
            ["Ingestion"] = "http://ingestion:8080",
            ["Flash P&L"] = "http://flash-pnl:8081",
            ["Regulatory"] = "http://regulatory:8082"
        };

        var results = new Dictionary<string, bool>();
        
        await Parallel.ForEachAsync(services, ct, async (kvp, token) =>
        {
            results[kvp.Key] = await CheckServiceHealthAsync(kvp.Value, token);
        });

        return results;
    }
}
