namespace Eod.Shared.Resilience;

/// <summary>
/// Interface for circuit breaker pattern implementation.
/// Follows Interface Segregation Principle - small, focused interface.
/// </summary>
public interface ICircuitBreaker
{
    /// <summary>
    /// Gets the current state of the circuit breaker.
    /// </summary>
    CircuitBreakerState State { get; }
    
    /// <summary>
    /// Gets the circuit breaker name for identification.
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Gets metrics about the circuit breaker.
    /// </summary>
    CircuitBreakerMetrics Metrics { get; }
    
    /// <summary>
    /// Executes an action through the circuit breaker.
    /// </summary>
    /// <param name="action">The action to execute</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <exception cref="CircuitBreakerOpenException">Thrown when the circuit is open</exception>
    Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Executes a function through the circuit breaker and returns a result.
    /// </summary>
    /// <typeparam name="T">The return type</typeparam>
    /// <param name="action">The function to execute</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result of the function</returns>
    /// <exception cref="CircuitBreakerOpenException">Thrown when the circuit is open</exception>
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Manually trips the circuit breaker to open state.
    /// </summary>
    void Trip();
    
    /// <summary>
    /// Manually resets the circuit breaker to closed state.
    /// </summary>
    void Reset();
    
    /// <summary>
    /// Event raised when the circuit state changes.
    /// </summary>
    event EventHandler<CircuitBreakerStateChangedEventArgs>? StateChanged;
}

/// <summary>
/// Metrics for monitoring circuit breaker behavior.
/// </summary>
public sealed record CircuitBreakerMetrics
{
    public long TotalRequests { get; init; }
    public long SuccessfulRequests { get; init; }
    public long FailedRequests { get; init; }
    public long RejectedRequests { get; init; }
    public int ConsecutiveFailures { get; init; }
    public int ConsecutiveSuccesses { get; init; }
    public DateTime? LastFailureTime { get; init; }
    public DateTime? LastSuccessTime { get; init; }
    public DateTime? CircuitOpenedTime { get; init; }
    public TimeSpan? TimeUntilHalfOpen { get; init; }
    
    public double SuccessRate => TotalRequests > 0 
        ? (double)SuccessfulRequests / TotalRequests * 100 
        : 0;
}

/// <summary>
/// Event arguments for circuit breaker state changes.
/// </summary>
public sealed class CircuitBreakerStateChangedEventArgs : EventArgs
{
    public required string CircuitBreakerName { get; init; }
    public required CircuitBreakerState PreviousState { get; init; }
    public required CircuitBreakerState NewState { get; init; }
    public required DateTime Timestamp { get; init; }
    public Exception? LastException { get; init; }
}

/// <summary>
/// Exception thrown when attempting to execute through an open circuit.
/// </summary>
public sealed class CircuitBreakerOpenException : Exception
{
    public string CircuitBreakerName { get; }
    public TimeSpan? TimeUntilHalfOpen { get; }
    
    public CircuitBreakerOpenException(string circuitBreakerName, TimeSpan? timeUntilHalfOpen = null)
        : base($"Circuit breaker '{circuitBreakerName}' is open. " +
               (timeUntilHalfOpen.HasValue 
                   ? $"Retry after {timeUntilHalfOpen.Value.TotalSeconds:F0} seconds." 
                   : "Please try again later."))
    {
        CircuitBreakerName = circuitBreakerName;
        TimeUntilHalfOpen = timeUntilHalfOpen;
    }
}
