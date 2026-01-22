using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Eod.Shared.Health;

/// <summary>
/// Base health check service that tracks readiness state.
/// </summary>
public class ServiceHealthCheck : IHealthCheck
{
    private volatile bool _isReady = false;
    private string _statusMessage = "Service starting...";

    public void SetReady(string message = "Service is ready")
    {
        _isReady = true;
        _statusMessage = message;
    }

    public void SetNotReady(string message)
    {
        _isReady = false;
        _statusMessage = message;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (_isReady)
        {
            return Task.FromResult(HealthCheckResult.Healthy(_statusMessage));
        }

        return Task.FromResult(HealthCheckResult.Unhealthy(_statusMessage));
    }
}
