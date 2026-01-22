using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Eod.Shared.Resilience;

/// <summary>
/// Thread-safe implementation of the Circuit Breaker pattern.
/// 
/// The Circuit Breaker pattern prevents cascading failures in distributed systems by:
/// 1. CLOSED: Normal operation - requests flow through, failures are counted
/// 2. OPEN: After threshold failures - requests are rejected immediately (fail fast)
/// 3. HALF-OPEN: After timeout - allows test requests to check if service recovered
/// 
/// This implementation uses:
/// - Lock-free counters for high performance
/// - Sliding window for failure tracking
/// - Configurable thresholds and timeouts
/// </summary>
public sealed class CircuitBreaker : ICircuitBreaker
{
    private readonly CircuitBreakerOptions _options;
    private readonly ILogger<CircuitBreaker>? _logger;
    private readonly ConcurrentQueue<DateTime> _failureTimestamps;
    private readonly object _stateLock = new();
    
    private CircuitBreakerState _state = CircuitBreakerState.Closed;
    private DateTime? _circuitOpenedTime;
    private int _consecutiveFailures;
    private int _halfOpenSuccesses;
    private Exception? _lastException;
    
    // Metrics
    private long _totalRequests;
    private long _successfulRequests;
    private long _failedRequests;
    private long _rejectedRequests;
    private DateTime? _lastFailureTime;
    private DateTime? _lastSuccessTime;

    public CircuitBreaker(CircuitBreakerOptions options, ILogger<CircuitBreaker>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
        _failureTimestamps = new ConcurrentQueue<DateTime>();
    }
    
    /// <inheritdoc/>
    public string Name => _options.Name;
    
    /// <inheritdoc/>
    public CircuitBreakerState State
    {
        get
        {
            lock (_stateLock)
            {
                // Check if we should transition from Open to HalfOpen
                if (_state == CircuitBreakerState.Open && ShouldTransitionToHalfOpen())
                {
                    TransitionTo(CircuitBreakerState.HalfOpen);
                }
                return _state;
            }
        }
    }
    
    /// <inheritdoc/>
    public CircuitBreakerMetrics Metrics => new()
    {
        TotalRequests = Interlocked.Read(ref _totalRequests),
        SuccessfulRequests = Interlocked.Read(ref _successfulRequests),
        FailedRequests = Interlocked.Read(ref _failedRequests),
        RejectedRequests = Interlocked.Read(ref _rejectedRequests),
        ConsecutiveFailures = _consecutiveFailures,
        ConsecutiveSuccesses = _halfOpenSuccesses,
        LastFailureTime = _lastFailureTime,
        LastSuccessTime = _lastSuccessTime,
        CircuitOpenedTime = _circuitOpenedTime,
        TimeUntilHalfOpen = GetTimeUntilHalfOpen()
    };
    
    /// <inheritdoc/>
    public event EventHandler<CircuitBreakerStateChangedEventArgs>? StateChanged;

    /// <inheritdoc/>
    public async Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(async ct =>
        {
            await action(ct);
            return true;
        }, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        
        Interlocked.Increment(ref _totalRequests);
        
        // Check circuit state
        var currentState = State; // This may trigger Open -> HalfOpen transition
        
        if (currentState == CircuitBreakerState.Open)
        {
            Interlocked.Increment(ref _rejectedRequests);
            throw new CircuitBreakerOpenException(_options.Name, GetTimeUntilHalfOpen());
        }
        
        try
        {
            var result = await action(cancellationToken);
            RecordSuccess();
            return result;
        }
        catch (Exception ex) when (ShouldRecordFailure(ex))
        {
            RecordFailure(ex);
            throw;
        }
    }
    
    /// <inheritdoc/>
    public void Trip()
    {
        lock (_stateLock)
        {
            if (_state != CircuitBreakerState.Open)
            {
                _logger?.LogWarning("Circuit breaker '{Name}' manually tripped", _options.Name);
                TransitionTo(CircuitBreakerState.Open);
            }
        }
    }
    
    /// <inheritdoc/>
    public void Reset()
    {
        lock (_stateLock)
        {
            _logger?.LogInformation("Circuit breaker '{Name}' manually reset", _options.Name);
            _consecutiveFailures = 0;
            _halfOpenSuccesses = 0;
            ClearFailureWindow();
            TransitionTo(CircuitBreakerState.Closed);
        }
    }
    
    private void RecordSuccess()
    {
        Interlocked.Increment(ref _successfulRequests);
        _lastSuccessTime = DateTime.UtcNow;
        
        lock (_stateLock)
        {
            _consecutiveFailures = 0;
            
            if (_state == CircuitBreakerState.HalfOpen)
            {
                _halfOpenSuccesses++;
                
                if (_halfOpenSuccesses >= _options.SuccessThresholdInHalfOpen)
                {
                    _logger?.LogInformation(
                        "Circuit breaker '{Name}' recovered after {SuccessCount} successful requests",
                        _options.Name, _halfOpenSuccesses);
                    
                    ClearFailureWindow();
                    TransitionTo(CircuitBreakerState.Closed);
                }
            }
        }
    }
    
    private void RecordFailure(Exception ex)
    {
        Interlocked.Increment(ref _failedRequests);
        var now = DateTime.UtcNow;
        _lastFailureTime = now;
        _lastException = ex;
        
        // Add to sliding window
        _failureTimestamps.Enqueue(now);
        CleanupFailureWindow(now);
        
        lock (_stateLock)
        {
            _consecutiveFailures++;
            
            _logger?.LogDebug(
                "Circuit breaker '{Name}' recorded failure {Count}/{Threshold}: {Message}",
                _options.Name, _consecutiveFailures, _options.FailureThreshold, ex.Message);
            
            if (_state == CircuitBreakerState.HalfOpen)
            {
                // Single failure in half-open trips back to open
                _logger?.LogWarning(
                    "Circuit breaker '{Name}' failed in half-open state, reopening",
                    _options.Name);
                
                _halfOpenSuccesses = 0;
                TransitionTo(CircuitBreakerState.Open);
            }
            else if (_state == CircuitBreakerState.Closed)
            {
                // Check if we should open the circuit
                var failuresInWindow = CountFailuresInWindow(now);
                
                if (failuresInWindow >= _options.FailureThreshold)
                {
                    _logger?.LogWarning(
                        "Circuit breaker '{Name}' opened after {FailureCount} failures in {Window}s window",
                        _options.Name, failuresInWindow, _options.FailureWindow.TotalSeconds);
                    
                    TransitionTo(CircuitBreakerState.Open);
                }
            }
        }
    }
    
    private bool ShouldRecordFailure(Exception ex)
    {
        // If no specific exception types configured, record all failures
        if (_options.ExceptionTypes.Length == 0)
            return true;
        
        // Check if exception type matches any configured types
        var exceptionType = ex.GetType();
        return _options.ExceptionTypes.Any(t => t.IsAssignableFrom(exceptionType));
    }
    
    private bool ShouldTransitionToHalfOpen()
    {
        if (_circuitOpenedTime == null)
            return false;
        
        return DateTime.UtcNow - _circuitOpenedTime.Value >= _options.OpenDuration;
    }
    
    private TimeSpan? GetTimeUntilHalfOpen()
    {
        if (_state != CircuitBreakerState.Open || _circuitOpenedTime == null)
            return null;
        
        var elapsed = DateTime.UtcNow - _circuitOpenedTime.Value;
        var remaining = _options.OpenDuration - elapsed;
        return remaining > TimeSpan.Zero ? remaining : null;
    }
    
    private int CountFailuresInWindow(DateTime now)
    {
        var windowStart = now - _options.FailureWindow;
        return _failureTimestamps.Count(t => t >= windowStart);
    }
    
    private void CleanupFailureWindow(DateTime now)
    {
        var windowStart = now - _options.FailureWindow;
        
        // Remove old failures outside the window
        while (_failureTimestamps.TryPeek(out var oldest) && oldest < windowStart)
        {
            _failureTimestamps.TryDequeue(out _);
        }
    }
    
    private void ClearFailureWindow()
    {
        while (_failureTimestamps.TryDequeue(out _)) { }
    }
    
    private void TransitionTo(CircuitBreakerState newState)
    {
        var previousState = _state;
        
        if (previousState == newState)
            return;
        
        _state = newState;
        
        if (newState == CircuitBreakerState.Open)
        {
            _circuitOpenedTime = DateTime.UtcNow;
            _halfOpenSuccesses = 0;
        }
        else if (newState == CircuitBreakerState.Closed)
        {
            _circuitOpenedTime = null;
            _halfOpenSuccesses = 0;
        }
        else if (newState == CircuitBreakerState.HalfOpen)
        {
            _halfOpenSuccesses = 0;
        }
        
        _logger?.LogInformation(
            "Circuit breaker '{Name}' state changed: {PreviousState} -> {NewState}",
            _options.Name, previousState, newState);
        
        StateChanged?.Invoke(this, new CircuitBreakerStateChangedEventArgs
        {
            CircuitBreakerName = _options.Name,
            PreviousState = previousState,
            NewState = newState,
            Timestamp = DateTime.UtcNow,
            LastException = _lastException
        });
    }
}
