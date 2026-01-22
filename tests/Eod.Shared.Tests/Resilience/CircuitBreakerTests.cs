using Eod.Shared.Resilience;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Eod.Shared.Tests.Resilience;

public class CircuitBreakerTests
{
    private readonly Mock<ILogger<CircuitBreaker>> _loggerMock;
    
    public CircuitBreakerTests()
    {
        _loggerMock = new Mock<ILogger<CircuitBreaker>>();
    }

    #region Initial State Tests
    
    [Fact]
    public void Constructor_InitializesInClosedState()
    {
        // Arrange & Act
        var cb = CreateCircuitBreaker();
        
        // Assert
        cb.State.Should().Be(CircuitBreakerState.Closed);
    }
    
    [Fact]
    public void Constructor_InitializesWithZeroMetrics()
    {
        // Arrange & Act
        var cb = CreateCircuitBreaker();
        
        // Assert
        cb.Metrics.TotalRequests.Should().Be(0);
        cb.Metrics.SuccessfulRequests.Should().Be(0);
        cb.Metrics.FailedRequests.Should().Be(0);
        cb.Metrics.RejectedRequests.Should().Be(0);
    }
    
    [Fact]
    public void Constructor_ThrowsOnNullOptions()
    {
        // Act & Assert
        var act = () => new CircuitBreaker(null!, _loggerMock.Object);
        act.Should().Throw<ArgumentNullException>();
    }
    
    #endregion
    
    #region Success Execution Tests
    
    [Fact]
    public async Task ExecuteAsync_WhenClosed_ExecutesAction()
    {
        // Arrange
        var cb = CreateCircuitBreaker();
        var executed = false;
        
        // Act
        await cb.ExecuteAsync(async _ =>
        {
            executed = true;
            await Task.CompletedTask;
        });
        
        // Assert
        executed.Should().BeTrue();
    }
    
    [Fact]
    public async Task ExecuteAsync_WhenClosed_ReturnsResult()
    {
        // Arrange
        var cb = CreateCircuitBreaker();
        
        // Act
        var result = await cb.ExecuteAsync(_ => Task.FromResult(42));
        
        // Assert
        result.Should().Be(42);
    }
    
    [Fact]
    public async Task ExecuteAsync_OnSuccess_IncrementsMetrics()
    {
        // Arrange
        var cb = CreateCircuitBreaker();
        
        // Act
        await cb.ExecuteAsync(_ => Task.FromResult(true));
        await cb.ExecuteAsync(_ => Task.FromResult(true));
        
        // Assert
        cb.Metrics.TotalRequests.Should().Be(2);
        cb.Metrics.SuccessfulRequests.Should().Be(2);
        cb.Metrics.FailedRequests.Should().Be(0);
    }
    
    #endregion
    
    #region Failure and State Transition Tests
    
    [Fact]
    public async Task ExecuteAsync_OnFailure_IncrementsFailureMetrics()
    {
        // Arrange
        var cb = CreateCircuitBreaker(new CircuitBreakerOptions { FailureThreshold = 10 });
        
        // Act
        try
        {
            await cb.ExecuteAsync<bool>(_ => throw new InvalidOperationException("Test"));
        }
        catch { }
        
        // Assert
        cb.Metrics.TotalRequests.Should().Be(1);
        cb.Metrics.FailedRequests.Should().Be(1);
    }
    
    [Fact]
    public async Task ExecuteAsync_AfterThresholdFailures_OpensCircuit()
    {
        // Arrange
        var cb = CreateCircuitBreaker(new CircuitBreakerOptions
        {
            FailureThreshold = 3,
            FailureWindow = TimeSpan.FromMinutes(5)
        });
        
        // Act - cause 3 failures
        for (int i = 0; i < 3; i++)
        {
            try
            {
                await cb.ExecuteAsync<bool>(_ => throw new InvalidOperationException($"Failure {i + 1}"));
            }
            catch { }
        }
        
        // Assert
        cb.State.Should().Be(CircuitBreakerState.Open);
    }
    
    [Fact]
    public async Task ExecuteAsync_WhenOpen_ThrowsCircuitBreakerOpenException()
    {
        // Arrange
        var cb = CreateCircuitBreaker(new CircuitBreakerOptions
        {
            FailureThreshold = 1,
            OpenDuration = TimeSpan.FromMinutes(5)
        });
        
        // Trip the circuit
        try
        {
            await cb.ExecuteAsync<bool>(_ => throw new InvalidOperationException("Trip"));
        }
        catch { }
        
        // Act & Assert
        var act = async () => await cb.ExecuteAsync(_ => Task.FromResult(true));
        await act.Should().ThrowAsync<CircuitBreakerOpenException>()
            .Where(ex => ex.CircuitBreakerName == cb.Name);
    }
    
    [Fact]
    public async Task ExecuteAsync_WhenOpen_IncrementsRejectedCount()
    {
        // Arrange
        var cb = CreateCircuitBreaker(new CircuitBreakerOptions
        {
            FailureThreshold = 1,
            OpenDuration = TimeSpan.FromMinutes(5)
        });
        
        // Trip the circuit
        try { await cb.ExecuteAsync<bool>(_ => throw new Exception()); } catch { }
        
        // Act - try to execute while open
        try { await cb.ExecuteAsync(_ => Task.FromResult(true)); } catch { }
        try { await cb.ExecuteAsync(_ => Task.FromResult(true)); } catch { }
        
        // Assert
        cb.Metrics.RejectedRequests.Should().Be(2);
    }
    
    #endregion
    
    #region Half-Open State Tests
    
    [Fact]
    public async Task State_AfterOpenDuration_TransitionsToHalfOpen()
    {
        // Arrange
        var cb = CreateCircuitBreaker(new CircuitBreakerOptions
        {
            FailureThreshold = 1,
            OpenDuration = TimeSpan.FromMilliseconds(50)
        });
        
        // Trip the circuit
        try { await cb.ExecuteAsync<bool>(_ => throw new Exception()); } catch { }
        cb.State.Should().Be(CircuitBreakerState.Open);
        
        // Act - wait for open duration
        await Task.Delay(100);
        
        // Assert
        cb.State.Should().Be(CircuitBreakerState.HalfOpen);
    }
    
    [Fact]
    public async Task ExecuteAsync_InHalfOpen_OnSuccess_ClosesCircuit()
    {
        // Arrange
        var cb = CreateCircuitBreaker(new CircuitBreakerOptions
        {
            FailureThreshold = 1,
            OpenDuration = TimeSpan.FromMilliseconds(50),
            SuccessThresholdInHalfOpen = 2
        });
        
        // Trip and wait for half-open
        try { await cb.ExecuteAsync<bool>(_ => throw new Exception()); } catch { }
        await Task.Delay(100);
        cb.State.Should().Be(CircuitBreakerState.HalfOpen);
        
        // Act - successful requests in half-open
        await cb.ExecuteAsync(_ => Task.FromResult(true));
        await cb.ExecuteAsync(_ => Task.FromResult(true));
        
        // Assert
        cb.State.Should().Be(CircuitBreakerState.Closed);
    }
    
    [Fact]
    public async Task ExecuteAsync_InHalfOpen_OnFailure_ReopensCircuit()
    {
        // Arrange
        var cb = CreateCircuitBreaker(new CircuitBreakerOptions
        {
            FailureThreshold = 1,
            OpenDuration = TimeSpan.FromMilliseconds(50),
            SuccessThresholdInHalfOpen = 5
        });
        
        // Trip and wait for half-open
        try { await cb.ExecuteAsync<bool>(_ => throw new Exception()); } catch { }
        await Task.Delay(100);
        cb.State.Should().Be(CircuitBreakerState.HalfOpen);
        
        // Act - failure in half-open
        try { await cb.ExecuteAsync<bool>(_ => throw new Exception()); } catch { }
        
        // Assert
        cb.State.Should().Be(CircuitBreakerState.Open);
    }
    
    #endregion
    
    #region Manual Control Tests
    
    [Fact]
    public void Trip_ManuallyOpensCircuit()
    {
        // Arrange
        var cb = CreateCircuitBreaker();
        
        // Act
        cb.Trip();
        
        // Assert
        cb.State.Should().Be(CircuitBreakerState.Open);
    }
    
    [Fact]
    public async Task Reset_ManuallyClosesCircuit()
    {
        // Arrange
        var cb = CreateCircuitBreaker(new CircuitBreakerOptions
        {
            FailureThreshold = 1,
            OpenDuration = TimeSpan.FromMinutes(5)
        });
        
        // Trip the circuit
        try { await cb.ExecuteAsync<bool>(_ => throw new Exception()); } catch { }
        cb.State.Should().Be(CircuitBreakerState.Open);
        
        // Act
        cb.Reset();
        
        // Assert
        cb.State.Should().Be(CircuitBreakerState.Closed);
        cb.Metrics.ConsecutiveFailures.Should().Be(0);
    }
    
    #endregion
    
    #region Event Tests
    
    [Fact]
    public async Task StateChanged_EventFiredOnTransition()
    {
        // Arrange
        var cb = CreateCircuitBreaker(new CircuitBreakerOptions
        {
            FailureThreshold = 1
        });
        
        CircuitBreakerStateChangedEventArgs? eventArgs = null;
        cb.StateChanged += (_, args) => eventArgs = args;
        
        // Act - trip the circuit
        try { await cb.ExecuteAsync<bool>(_ => throw new Exception()); } catch { }
        
        // Assert
        eventArgs.Should().NotBeNull();
        eventArgs!.PreviousState.Should().Be(CircuitBreakerState.Closed);
        eventArgs.NewState.Should().Be(CircuitBreakerState.Open);
    }
    
    [Fact]
    public async Task StateChanged_IncludesLastException()
    {
        // Arrange
        var cb = CreateCircuitBreaker(new CircuitBreakerOptions { FailureThreshold = 1 });
        
        CircuitBreakerStateChangedEventArgs? eventArgs = null;
        cb.StateChanged += (_, args) => eventArgs = args;
        
        var expectedException = new InvalidOperationException("Test exception");
        
        // Act
        try { await cb.ExecuteAsync<bool>(_ => throw expectedException); } catch { }
        
        // Assert
        eventArgs!.LastException.Should().Be(expectedException);
    }
    
    #endregion
    
    #region Exception Type Filtering Tests
    
    [Fact]
    public async Task ExecuteAsync_WithConfiguredExceptionTypes_OnlyTripsOnMatchingExceptions()
    {
        // Arrange
        var cb = CreateCircuitBreaker(new CircuitBreakerOptions
        {
            FailureThreshold = 1,
            ExceptionTypes = [typeof(HttpRequestException)]
        });
        
        // Act - throw non-matching exception
        try
        {
            await cb.ExecuteAsync<bool>(_ => throw new InvalidOperationException());
        }
        catch { }
        
        // Assert - circuit should stay closed
        cb.State.Should().Be(CircuitBreakerState.Closed);
    }
    
    [Fact]
    public async Task ExecuteAsync_WithConfiguredExceptionTypes_TripsOnMatchingException()
    {
        // Arrange
        var cb = CreateCircuitBreaker(new CircuitBreakerOptions
        {
            FailureThreshold = 1,
            ExceptionTypes = [typeof(HttpRequestException)]
        });
        
        // Act - throw matching exception
        try
        {
            await cb.ExecuteAsync<bool>(_ => throw new HttpRequestException());
        }
        catch { }
        
        // Assert
        cb.State.Should().Be(CircuitBreakerState.Open);
    }
    
    #endregion
    
    #region Sliding Window Tests
    
    [Fact]
    public async Task ExecuteAsync_FailuresOutsideWindow_DoNotTripCircuit()
    {
        // Arrange
        var cb = CreateCircuitBreaker(new CircuitBreakerOptions
        {
            FailureThreshold = 3,
            FailureWindow = TimeSpan.FromMilliseconds(100)
        });
        
        // Act - cause failures with delays
        try { await cb.ExecuteAsync<bool>(_ => throw new Exception()); } catch { }
        await Task.Delay(150); // Wait for failure to expire from window
        try { await cb.ExecuteAsync<bool>(_ => throw new Exception()); } catch { }
        await Task.Delay(150);
        try { await cb.ExecuteAsync<bool>(_ => throw new Exception()); } catch { }
        
        // Assert - should not open because failures are outside window
        cb.State.Should().Be(CircuitBreakerState.Closed);
    }
    
    [Fact]
    public async Task ExecuteAsync_FailuresWithinWindow_TripCircuit()
    {
        // Arrange
        var cb = CreateCircuitBreaker(new CircuitBreakerOptions
        {
            FailureThreshold = 3,
            FailureWindow = TimeSpan.FromSeconds(10)
        });
        
        // Act - cause rapid failures within window
        for (int i = 0; i < 3; i++)
        {
            try { await cb.ExecuteAsync<bool>(_ => throw new Exception()); } catch { }
        }
        
        // Assert
        cb.State.Should().Be(CircuitBreakerState.Open);
    }
    
    #endregion
    
    #region Thread Safety Tests
    
    [Fact]
    public async Task ExecuteAsync_IsConcurrencySafe()
    {
        // Arrange
        var cb = CreateCircuitBreaker(new CircuitBreakerOptions
        {
            FailureThreshold = 100,
            FailureWindow = TimeSpan.FromMinutes(5)
        });
        
        // Act - run many concurrent operations
        var tasks = Enumerable.Range(0, 100).Select(async i =>
        {
            if (i % 2 == 0)
            {
                await cb.ExecuteAsync(_ => Task.FromResult(true));
            }
            else
            {
                try { await cb.ExecuteAsync<bool>(_ => throw new Exception()); } catch { }
            }
        });
        
        await Task.WhenAll(tasks);
        
        // Assert - metrics should be accurate
        cb.Metrics.TotalRequests.Should().Be(100);
        cb.Metrics.SuccessfulRequests.Should().Be(50);
        cb.Metrics.FailedRequests.Should().Be(50);
    }
    
    #endregion
    
    #region Metrics Tests
    
    [Fact]
    public async Task Metrics_TracksLastSuccessAndFailureTimes()
    {
        // Arrange
        var cb = CreateCircuitBreaker(new CircuitBreakerOptions { FailureThreshold = 10 });
        var beforeTest = DateTime.UtcNow;
        
        // Act
        await cb.ExecuteAsync(_ => Task.FromResult(true));
        try { await cb.ExecuteAsync<bool>(_ => throw new Exception()); } catch { }
        
        var afterTest = DateTime.UtcNow;
        
        // Assert
        cb.Metrics.LastSuccessTime.Should().NotBeNull();
        cb.Metrics.LastSuccessTime.Should().BeOnOrAfter(beforeTest);
        cb.Metrics.LastSuccessTime.Should().BeOnOrBefore(afterTest);
        
        cb.Metrics.LastFailureTime.Should().NotBeNull();
        cb.Metrics.LastFailureTime.Should().BeOnOrAfter(beforeTest);
    }
    
    [Fact]
    public void Metrics_SuccessRate_CalculatesCorrectly()
    {
        // Arrange
        var metrics = new CircuitBreakerMetrics
        {
            TotalRequests = 100,
            SuccessfulRequests = 75,
            FailedRequests = 25
        };
        
        // Assert
        metrics.SuccessRate.Should().Be(75.0);
    }
    
    [Fact]
    public void Metrics_SuccessRate_ReturnsZeroWhenNoRequests()
    {
        // Arrange
        var metrics = new CircuitBreakerMetrics { TotalRequests = 0 };
        
        // Assert
        metrics.SuccessRate.Should().Be(0);
    }
    
    #endregion
    
    private CircuitBreaker CreateCircuitBreaker(CircuitBreakerOptions? options = null)
    {
        return new CircuitBreaker(
            options ?? new CircuitBreakerOptions { Name = "Test" },
            _loggerMock.Object);
    }
}
