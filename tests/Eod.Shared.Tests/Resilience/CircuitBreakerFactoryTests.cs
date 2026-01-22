using Eod.Shared.Resilience;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Eod.Shared.Tests.Resilience;

public class CircuitBreakerFactoryTests
{
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;
    
    public CircuitBreakerFactoryTests()
    {
        _loggerFactoryMock = new Mock<ILoggerFactory>();
        _loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(Mock.Of<ILogger>());
    }
    
    [Fact]
    public void GetOrCreate_CreatesNewCircuitBreaker()
    {
        // Arrange
        var factory = new CircuitBreakerFactory(_loggerFactoryMock.Object);
        
        // Act
        var cb = factory.GetOrCreate("test");
        
        // Assert
        cb.Should().NotBeNull();
        cb.Name.Should().Be("test");
    }
    
    [Fact]
    public void GetOrCreate_ReturnsSameInstanceForSameName()
    {
        // Arrange
        var factory = new CircuitBreakerFactory(_loggerFactoryMock.Object);
        
        // Act
        var cb1 = factory.GetOrCreate("test");
        var cb2 = factory.GetOrCreate("test");
        
        // Assert
        cb1.Should().BeSameAs(cb2);
    }
    
    [Fact]
    public void GetOrCreate_ReturnsDifferentInstancesForDifferentNames()
    {
        // Arrange
        var factory = new CircuitBreakerFactory(_loggerFactoryMock.Object);
        
        // Act
        var cb1 = factory.GetOrCreate("test1");
        var cb2 = factory.GetOrCreate("test2");
        
        // Assert
        cb1.Should().NotBeSameAs(cb2);
        cb1.Name.Should().Be("test1");
        cb2.Name.Should().Be("test2");
    }
    
    [Fact]
    public void GetOrCreate_UsesProvidedOptions()
    {
        // Arrange
        var factory = new CircuitBreakerFactory(_loggerFactoryMock.Object);
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 10,
            OpenDuration = TimeSpan.FromMinutes(5)
        };
        
        // Act
        var cb = factory.GetOrCreate("test", options);
        
        // Assert
        cb.Should().NotBeNull();
    }
    
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetOrCreate_ThrowsOnInvalidName(string? name)
    {
        // Arrange
        var factory = new CircuitBreakerFactory(_loggerFactoryMock.Object);
        
        // Act & Assert
        var act = () => factory.GetOrCreate(name!);
        act.Should().Throw<ArgumentException>();
    }
    
    [Fact]
    public void Get_ReturnsExistingCircuitBreaker()
    {
        // Arrange
        var factory = new CircuitBreakerFactory(_loggerFactoryMock.Object);
        var created = factory.GetOrCreate("test");
        
        // Act
        var retrieved = factory.Get("test");
        
        // Assert
        retrieved.Should().BeSameAs(created);
    }
    
    [Fact]
    public void Get_ReturnsNullForNonExistent()
    {
        // Arrange
        var factory = new CircuitBreakerFactory(_loggerFactoryMock.Object);
        
        // Act
        var cb = factory.Get("non-existent");
        
        // Assert
        cb.Should().BeNull();
    }
    
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Get_ThrowsOnInvalidName(string? name)
    {
        // Arrange
        var factory = new CircuitBreakerFactory(_loggerFactoryMock.Object);
        
        // Act & Assert
        var act = () => factory.Get(name!);
        act.Should().Throw<ArgumentException>();
    }
    
    [Fact]
    public void GetAll_ReturnsAllCircuitBreakers()
    {
        // Arrange
        var factory = new CircuitBreakerFactory(_loggerFactoryMock.Object);
        factory.GetOrCreate("cb1");
        factory.GetOrCreate("cb2");
        factory.GetOrCreate("cb3");
        
        // Act
        var all = factory.GetAll();
        
        // Assert
        all.Should().HaveCount(3);
        all.Select(cb => cb.Name).Should().BeEquivalentTo(["cb1", "cb2", "cb3"]);
    }
    
    [Fact]
    public void GetAll_ReturnsEmptyWhenNoCircuitBreakers()
    {
        // Arrange
        var factory = new CircuitBreakerFactory(_loggerFactoryMock.Object);
        
        // Act
        var all = factory.GetAll();
        
        // Assert
        all.Should().BeEmpty();
    }
    
    [Fact]
    public async Task ResetAll_ResetsAllCircuitBreakers()
    {
        // Arrange
        var factory = new CircuitBreakerFactory(_loggerFactoryMock.Object);
        var cb1 = factory.GetOrCreate("cb1", new CircuitBreakerOptions { FailureThreshold = 1 });
        var cb2 = factory.GetOrCreate("cb2", new CircuitBreakerOptions { FailureThreshold = 1 });
        
        // Trip both circuit breakers
        try { await cb1.ExecuteAsync<bool>(_ => throw new Exception()); } catch { }
        try { await cb2.ExecuteAsync<bool>(_ => throw new Exception()); } catch { }
        
        cb1.State.Should().Be(CircuitBreakerState.Open);
        cb2.State.Should().Be(CircuitBreakerState.Open);
        
        // Act
        factory.ResetAll();
        
        // Assert
        cb1.State.Should().Be(CircuitBreakerState.Closed);
        cb2.State.Should().Be(CircuitBreakerState.Closed);
    }
    
    [Fact]
    public void Constructor_WorksWithoutLoggerFactory()
    {
        // Act
        var factory = new CircuitBreakerFactory();
        var cb = factory.GetOrCreate("test");
        
        // Assert
        cb.Should().NotBeNull();
    }
}
