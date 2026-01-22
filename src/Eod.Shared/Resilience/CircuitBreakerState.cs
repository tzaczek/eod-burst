namespace Eod.Shared.Resilience;

/// <summary>
/// Represents the state of a circuit breaker.
/// </summary>
public enum CircuitBreakerState
{
    /// <summary>
    /// Circuit is closed - requests flow normally.
    /// </summary>
    Closed,
    
    /// <summary>
    /// Circuit is open - requests are rejected immediately.
    /// </summary>
    Open,
    
    /// <summary>
    /// Circuit is half-open - allowing test requests to check if service recovered.
    /// </summary>
    HalfOpen
}
