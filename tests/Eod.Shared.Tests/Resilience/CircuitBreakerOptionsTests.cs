using Eod.Shared.Resilience;
using FluentAssertions;
using Xunit;

namespace Eod.Shared.Tests.Resilience;

public class CircuitBreakerOptionsTests
{
    [Fact]
    public void DefaultOptions_HasSensibleDefaults()
    {
        // Act
        var options = new CircuitBreakerOptions();
        
        // Assert
        options.FailureThreshold.Should().Be(5);
        options.OpenDuration.Should().Be(TimeSpan.FromSeconds(30));
        options.SuccessThresholdInHalfOpen.Should().Be(2);
        options.FailureWindow.Should().Be(TimeSpan.FromSeconds(60));
        options.ExceptionTypes.Should().BeEmpty();
        options.Name.Should().Be("default");
    }
    
    [Fact]
    public void HighAvailability_HasAggressiveSettings()
    {
        // Act
        var options = CircuitBreakerOptions.HighAvailability;
        
        // Assert
        options.FailureThreshold.Should().Be(3);
        options.OpenDuration.Should().Be(TimeSpan.FromSeconds(15));
        options.SuccessThresholdInHalfOpen.Should().Be(1);
        options.FailureWindow.Should().Be(TimeSpan.FromSeconds(30));
    }
    
    [Fact]
    public void ExternalService_HasConservativeSettings()
    {
        // Act
        var options = CircuitBreakerOptions.ExternalService;
        
        // Assert
        options.FailureThreshold.Should().Be(5);
        options.OpenDuration.Should().Be(TimeSpan.FromSeconds(60));
        options.SuccessThresholdInHalfOpen.Should().Be(3);
        options.FailureWindow.Should().Be(TimeSpan.FromSeconds(120));
    }
    
    [Fact]
    public void Storage_HasBalancedSettings()
    {
        // Act
        var options = CircuitBreakerOptions.Storage;
        
        // Assert
        options.FailureThreshold.Should().Be(10);
        options.OpenDuration.Should().Be(TimeSpan.FromSeconds(30));
        options.SuccessThresholdInHalfOpen.Should().Be(2);
        options.FailureWindow.Should().Be(TimeSpan.FromSeconds(60));
    }
    
    [Fact]
    public void Record_SupportsWithExpression()
    {
        // Arrange
        var original = new CircuitBreakerOptions
        {
            Name = "Original",
            FailureThreshold = 5
        };
        
        // Act
        var modified = original with
        {
            Name = "Modified",
            FailureThreshold = 10
        };
        
        // Assert
        modified.Name.Should().Be("Modified");
        modified.FailureThreshold.Should().Be(10);
        modified.OpenDuration.Should().Be(original.OpenDuration); // Unchanged
    }
}
