namespace Eod.Shared.Resilience;

/// <summary>
/// Configuration options for the circuit breaker.
/// Follows the Builder pattern for fluent configuration.
/// </summary>
public sealed record CircuitBreakerOptions
{
    /// <summary>
    /// Number of consecutive failures before the circuit opens.
    /// Default: 5
    /// </summary>
    public int FailureThreshold { get; init; } = 5;
    
    /// <summary>
    /// Time the circuit stays open before transitioning to half-open.
    /// Default: 30 seconds
    /// </summary>
    public TimeSpan OpenDuration { get; init; } = TimeSpan.FromSeconds(30);
    
    /// <summary>
    /// Number of successful calls in half-open state before closing.
    /// Default: 2
    /// </summary>
    public int SuccessThresholdInHalfOpen { get; init; } = 2;
    
    /// <summary>
    /// Time window to count failures. Failures outside this window are ignored.
    /// Default: 60 seconds
    /// </summary>
    public TimeSpan FailureWindow { get; init; } = TimeSpan.FromSeconds(60);
    
    /// <summary>
    /// Types of exceptions that should trip the circuit breaker.
    /// Empty means all exceptions trip the breaker.
    /// </summary>
    public Type[] ExceptionTypes { get; init; } = [];
    
    /// <summary>
    /// Name of the circuit breaker for logging and monitoring.
    /// </summary>
    public string Name { get; init; } = "default";
    
    /// <summary>
    /// Creates default options for high-availability scenarios.
    /// </summary>
    public static CircuitBreakerOptions HighAvailability => new()
    {
        FailureThreshold = 3,
        OpenDuration = TimeSpan.FromSeconds(15),
        SuccessThresholdInHalfOpen = 1,
        FailureWindow = TimeSpan.FromSeconds(30)
    };
    
    /// <summary>
    /// Creates default options for external service calls.
    /// </summary>
    public static CircuitBreakerOptions ExternalService => new()
    {
        FailureThreshold = 5,
        OpenDuration = TimeSpan.FromSeconds(60),
        SuccessThresholdInHalfOpen = 3,
        FailureWindow = TimeSpan.FromSeconds(120)
    };
    
    /// <summary>
    /// Creates default options for storage operations.
    /// </summary>
    public static CircuitBreakerOptions Storage => new()
    {
        FailureThreshold = 10,
        OpenDuration = TimeSpan.FromSeconds(30),
        SuccessThresholdInHalfOpen = 2,
        FailureWindow = TimeSpan.FromSeconds(60)
    };
}
