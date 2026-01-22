using Eod.FlashPnl.Services;
using Eod.Shared.Models;
using Eod.Shared.Redis;
using Eod.Shared.Resilience;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Eod.FlashPnl.Tests.Services;

/// <summary>
/// Tests for ResilientRedisService circuit breaker behavior.
/// These tests verify that:
/// 1. Circuit breaker protects against cascading failures
/// 2. Operations fail fast when circuit is open
/// 3. System recovers automatically when Redis becomes available
/// </summary>
public class ResilientRedisServiceTests
{
    private readonly Mock<IRedisService> _mockRedis;
    private readonly Mock<ICircuitBreakerFactory> _mockFactory;
    private readonly Mock<ICircuitBreaker> _mockPublishBreaker;
    private readonly Mock<ICircuitBreaker> _mockQueryBreaker;
    private readonly Mock<ILogger<ResilientRedisService>> _mockLogger;

    public ResilientRedisServiceTests()
    {
        _mockRedis = new Mock<IRedisService>();
        _mockPublishBreaker = new Mock<ICircuitBreaker>();
        _mockQueryBreaker = new Mock<ICircuitBreaker>();
        _mockFactory = new Mock<ICircuitBreakerFactory>();
        _mockLogger = new Mock<ILogger<ResilientRedisService>>();
        
        // Setup factory to return our mocked circuit breakers
        _mockFactory
            .Setup(f => f.GetOrCreate("FlashPnl-Redis-Publish", It.IsAny<CircuitBreakerOptions>()))
            .Returns(_mockPublishBreaker.Object);
        _mockFactory
            .Setup(f => f.GetOrCreate("FlashPnl-Redis-Query", It.IsAny<CircuitBreakerOptions>()))
            .Returns(_mockQueryBreaker.Object);
        
        // Default: circuits are closed
        _mockPublishBreaker.Setup(b => b.State).Returns(CircuitBreakerState.Closed);
        _mockQueryBreaker.Setup(b => b.State).Returns(CircuitBreakerState.Closed);
        
        // Default metrics
        var defaultMetrics = new CircuitBreakerMetrics
        {
            TotalRequests = 0,
            SuccessfulRequests = 0,
            FailedRequests = 0,
            RejectedRequests = 0
        };
        _mockPublishBreaker.Setup(b => b.Metrics).Returns(defaultMetrics);
        _mockQueryBreaker.Setup(b => b.Metrics).Returns(defaultMetrics);
    }

    #region UpdateAndPublishAsync Tests

    [Fact]
    public async Task UpdateAndPublishAsync_WhenCircuitClosed_ShouldPublishSuccessfully()
    {
        // Arrange
        var snapshot = CreateTestSnapshot("trader1", "AAPL");
        
        _mockPublishBreaker
            .Setup(b => b.ExecuteAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        
        var service = CreateService();

        // Act
        var result = await service.UpdateAndPublishAsync(snapshot);

        // Assert
        result.Should().BeTrue();
        _mockPublishBreaker.Verify(
            b => b.ExecuteAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()), 
            Times.Once);
    }

    [Fact]
    public async Task UpdateAndPublishAsync_WhenCircuitOpen_ShouldSkipAndReturnFalse()
    {
        // Arrange
        var snapshot = CreateTestSnapshot("trader1", "AAPL");
        _mockPublishBreaker.Setup(b => b.State).Returns(CircuitBreakerState.Open);
        
        var service = CreateService();

        // Act
        var result = await service.UpdateAndPublishAsync(snapshot);

        // Assert
        result.Should().BeFalse();
        _mockPublishBreaker.Verify(
            b => b.ExecuteAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()), 
            Times.Never);
    }

    [Fact]
    public async Task UpdateAndPublishAsync_WhenCircuitBreakerThrowsOpenException_ShouldReturnFalse()
    {
        // Arrange
        var snapshot = CreateTestSnapshot("trader1", "AAPL");
        
        _mockPublishBreaker
            .Setup(b => b.ExecuteAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CircuitBreakerOpenException("FlashPnl-Redis-Publish"));
        
        var service = CreateService();

        // Act
        var result = await service.UpdateAndPublishAsync(snapshot);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAndPublishAsync_WhenRedisThrowsException_ShouldReturnFalseAndNotPropagate()
    {
        // Arrange
        var snapshot = CreateTestSnapshot("trader1", "AAPL");
        
        _mockPublishBreaker
            .Setup(b => b.ExecuteAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Redis timeout"));
        
        var service = CreateService();

        // Act - should not throw
        var result = await service.UpdateAndPublishAsync(snapshot);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region GetMarkPriceAsync Tests

    [Fact]
    public async Task GetMarkPriceAsync_WhenCircuitClosed_ShouldReturnPrice()
    {
        // Arrange
        var expectedPrice = (15050000000L, "LTP");
        
        _mockQueryBreaker
            .Setup(b => b.ExecuteAsync(
                It.IsAny<Func<CancellationToken, Task<(long, string)>>>(), 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedPrice);
        
        var service = CreateService();

        // Act
        var result = await service.GetMarkPriceAsync("AAPL");

        // Assert
        result.Should().NotBeNull();
        result!.Value.PriceMantissa.Should().Be(15050000000L);
        result.Value.Source.Should().Be("LTP");
    }

    [Fact]
    public async Task GetMarkPriceAsync_WhenCircuitOpen_ShouldReturnNull()
    {
        // Arrange
        _mockQueryBreaker.Setup(b => b.State).Returns(CircuitBreakerState.Open);
        
        var service = CreateService();

        // Act
        var result = await service.GetMarkPriceAsync("AAPL");

        // Assert
        result.Should().BeNull();
        _mockQueryBreaker.Verify(
            b => b.ExecuteAsync(
                It.IsAny<Func<CancellationToken, Task<(long, string)>>>(), 
                It.IsAny<CancellationToken>()), 
            Times.Never);
    }

    [Fact]
    public async Task GetMarkPriceAsync_WhenRedisThrows_ShouldReturnNullAndNotPropagate()
    {
        // Arrange
        _mockQueryBreaker
            .Setup(b => b.ExecuteAsync(
                It.IsAny<Func<CancellationToken, Task<(long, string)>>>(), 
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Redis timeout"));
        
        var service = CreateService();

        // Act - should not throw
        var result = await service.GetMarkPriceAsync("AAPL");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region SetPriceAsync Tests

    [Fact]
    public async Task SetPriceAsync_WhenCircuitClosed_ShouldSetPrice()
    {
        // Arrange
        _mockPublishBreaker
            .Setup(b => b.ExecuteAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        
        var service = CreateService();

        // Act
        await service.SetPriceAsync("AAPL", "ltp", 15050000000L);

        // Assert
        _mockPublishBreaker.Verify(
            b => b.ExecuteAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()), 
            Times.Once);
    }

    [Fact]
    public async Task SetPriceAsync_WhenCircuitOpen_ShouldSkipSilently()
    {
        // Arrange
        _mockPublishBreaker.Setup(b => b.State).Returns(CircuitBreakerState.Open);
        
        var service = CreateService();

        // Act
        await service.SetPriceAsync("AAPL", "ltp", 15050000000L);

        // Assert
        _mockPublishBreaker.Verify(
            b => b.ExecuteAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()), 
            Times.Never);
    }

    #endregion

    #region Circuit State Properties Tests

    [Theory]
    [InlineData(CircuitBreakerState.Closed)]
    [InlineData(CircuitBreakerState.Open)]
    [InlineData(CircuitBreakerState.HalfOpen)]
    public void PublishCircuitState_ShouldReflectUnderlyingState(CircuitBreakerState expectedState)
    {
        // Arrange
        _mockPublishBreaker.Setup(b => b.State).Returns(expectedState);
        
        var service = CreateService();

        // Act & Assert
        service.PublishCircuitState.Should().Be(expectedState);
    }

    [Theory]
    [InlineData(CircuitBreakerState.Closed)]
    [InlineData(CircuitBreakerState.Open)]
    [InlineData(CircuitBreakerState.HalfOpen)]
    public void QueryCircuitState_ShouldReflectUnderlyingState(CircuitBreakerState expectedState)
    {
        // Arrange
        _mockQueryBreaker.Setup(b => b.State).Returns(expectedState);
        
        var service = CreateService();

        // Act & Assert
        service.QueryCircuitState.Should().Be(expectedState);
    }

    #endregion

    #region Metrics Tests

    [Fact]
    public void PublishMetrics_ShouldExposeCircuitBreakerMetrics()
    {
        // Arrange
        var metrics = new CircuitBreakerMetrics
        {
            TotalRequests = 100,
            SuccessfulRequests = 90,
            FailedRequests = 10,
            RejectedRequests = 5
        };
        _mockPublishBreaker.Setup(b => b.Metrics).Returns(metrics);
        
        var service = CreateService();

        // Act
        var result = service.PublishMetrics;

        // Assert
        result.TotalRequests.Should().Be(100);
        result.SuccessfulRequests.Should().Be(90);
        result.FailedRequests.Should().Be(10);
        result.RejectedRequests.Should().Be(5);
    }

    [Fact]
    public async Task PublishFailures_ShouldIncrementOnFailure()
    {
        // Arrange
        var snapshot = CreateTestSnapshot("trader1", "AAPL");
        
        _mockPublishBreaker
            .Setup(b => b.ExecuteAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Redis timeout"));
        
        var service = CreateService();

        // Act
        await service.UpdateAndPublishAsync(snapshot);
        await service.UpdateAndPublishAsync(snapshot);

        // Assert
        service.PublishFailures.Should().Be(2);
    }

    [Fact]
    public async Task CircuitBreakerRejections_ShouldIncrementWhenCircuitOpen()
    {
        // Arrange
        var snapshot = CreateTestSnapshot("trader1", "AAPL");
        _mockPublishBreaker.Setup(b => b.State).Returns(CircuitBreakerState.Open);
        
        var service = CreateService();

        // Act
        await service.UpdateAndPublishAsync(snapshot);
        await service.UpdateAndPublishAsync(snapshot);
        await service.UpdateAndPublishAsync(snapshot);

        // Assert
        service.CircuitBreakerRejections.Should().Be(3);
    }

    #endregion

    #region State Change Event Tests

    [Fact]
    public void ShouldLogCircuitStateChanges()
    {
        // Arrange
        var service = CreateService();
        
        // Act - Simulate state change event
        _mockPublishBreaker.Raise(
            b => b.StateChanged += null!,
            this,
            new CircuitBreakerStateChangedEventArgs
            {
                CircuitBreakerName = "FlashPnl-Redis-Publish",
                PreviousState = CircuitBreakerState.Closed,
                NewState = CircuitBreakerState.Open,
                Timestamp = DateTime.UtcNow
            });

        // Assert - Verify logger was called (just ensure no exception)
        // In real tests, you could verify specific log message format
    }

    #endregion

    #region Helper Methods

    private ResilientRedisService CreateService()
    {
        return new ResilientRedisService(
            _mockRedis.Object,
            _mockFactory.Object,
            _mockLogger.Object);
    }

    private static PositionSnapshot CreateTestSnapshot(string traderId, string symbol)
    {
        return new PositionSnapshot
        {
            TraderId = traderId,
            Symbol = symbol,
            Quantity = 100,
            RealizedPnlMantissa = 2500000L,
            UnrealizedPnlMantissa = 2500000L,
            MarkPriceMantissa = 15050000000L,
            MarkSource = "LTP",
            TradeCount = 5,
            LastUpdateTicks = DateTime.UtcNow.Ticks
        };
    }

    #endregion
}

/// <summary>
/// Integration tests for circuit breaker recovery scenarios.
/// These test the full flow from failure to recovery.
/// </summary>
public class ResilientRedisServiceIntegrationTests
{
    [Fact]
    public async Task CircuitBreaker_ShouldOpenAfterThresholdFailures()
    {
        // This is a conceptual test showing expected behavior
        // In production, you would use real circuit breaker instance
        
        // Given: A circuit breaker configured with failure threshold of 5
        // When: 5 consecutive failures occur within the failure window
        // Then: Circuit opens and subsequent calls fail immediately
        
        var factory = new CircuitBreakerFactory();
        var options = new CircuitBreakerOptions
        {
            Name = "TestCircuit",
            FailureThreshold = 3,
            OpenDuration = TimeSpan.FromMilliseconds(100),
            FailureWindow = TimeSpan.FromSeconds(60)
        };
        
        var breaker = factory.GetOrCreate("TestCircuit", options);
        
        // Record failures
        for (int i = 0; i < 3; i++)
        {
            try
            {
                await breaker.ExecuteAsync(_ =>
                {
                    throw new TimeoutException("Simulated failure");
                });
            }
            catch (TimeoutException) { }
        }
        
        // Circuit should be open
        breaker.State.Should().Be(CircuitBreakerState.Open);
        
        // Subsequent calls should throw CircuitBreakerOpenException
        var act = async () => await breaker.ExecuteAsync(ct => Task.CompletedTask);
        await act.Should().ThrowAsync<CircuitBreakerOpenException>();
    }

    [Fact]
    public async Task CircuitBreaker_ShouldRecoverInHalfOpenState()
    {
        // Given: An open circuit breaker
        // When: OpenDuration passes and a successful request occurs
        // Then: Circuit transitions to Closed
        
        var factory = new CircuitBreakerFactory();
        var options = new CircuitBreakerOptions
        {
            Name = "RecoveryTestCircuit",
            FailureThreshold = 1,
            OpenDuration = TimeSpan.FromMilliseconds(50),
            SuccessThresholdInHalfOpen = 1,
            FailureWindow = TimeSpan.FromSeconds(60)
        };
        
        var breaker = factory.GetOrCreate("RecoveryTestCircuit", options);
        
        // Trip the circuit
        try
        {
            await breaker.ExecuteAsync(ct => throw new TimeoutException("Trip it"));
        }
        catch (TimeoutException) { }
        
        breaker.State.Should().Be(CircuitBreakerState.Open);
        
        // Wait for open duration
        await Task.Delay(60);
        
        // State should transition to HalfOpen on next check
        breaker.State.Should().Be(CircuitBreakerState.HalfOpen);
        
        // Successful request should close the circuit
        await breaker.ExecuteAsync(ct => Task.CompletedTask);
        
        breaker.State.Should().Be(CircuitBreakerState.Closed);
    }
}
