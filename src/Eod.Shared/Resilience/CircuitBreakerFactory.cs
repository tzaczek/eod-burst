using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Eod.Shared.Resilience;

/// <summary>
/// Factory for creating and managing named circuit breakers.
/// Implements the Factory pattern with singleton management per name.
/// </summary>
public interface ICircuitBreakerFactory
{
    /// <summary>
    /// Gets or creates a circuit breaker with the specified name and options.
    /// </summary>
    ICircuitBreaker GetOrCreate(string name, CircuitBreakerOptions? options = null);
    
    /// <summary>
    /// Gets a circuit breaker by name if it exists.
    /// </summary>
    ICircuitBreaker? Get(string name);
    
    /// <summary>
    /// Gets all registered circuit breakers.
    /// </summary>
    IReadOnlyCollection<ICircuitBreaker> GetAll();
    
    /// <summary>
    /// Resets all circuit breakers.
    /// </summary>
    void ResetAll();
}

/// <summary>
/// Implementation of circuit breaker factory.
/// Thread-safe singleton management of circuit breakers.
/// </summary>
public sealed class CircuitBreakerFactory : ICircuitBreakerFactory
{
    private readonly ConcurrentDictionary<string, ICircuitBreaker> _circuitBreakers = new();
    private readonly ILoggerFactory? _loggerFactory;
    
    public CircuitBreakerFactory(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory;
    }
    
    /// <inheritdoc/>
    public ICircuitBreaker GetOrCreate(string name, CircuitBreakerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        
        return _circuitBreakers.GetOrAdd(name, n =>
        {
            var cbOptions = options ?? new CircuitBreakerOptions { Name = n };
            var logger = _loggerFactory?.CreateLogger<CircuitBreaker>();
            return new CircuitBreaker(cbOptions with { Name = n }, logger);
        });
    }
    
    /// <inheritdoc/>
    public ICircuitBreaker? Get(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _circuitBreakers.TryGetValue(name, out var cb) ? cb : null;
    }
    
    /// <inheritdoc/>
    public IReadOnlyCollection<ICircuitBreaker> GetAll()
    {
        return _circuitBreakers.Values.ToList();
    }
    
    /// <inheritdoc/>
    public void ResetAll()
    {
        foreach (var cb in _circuitBreakers.Values)
        {
            cb.Reset();
        }
    }
}
