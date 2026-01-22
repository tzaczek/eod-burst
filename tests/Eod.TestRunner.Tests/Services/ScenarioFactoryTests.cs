using Eod.TestRunner.Models;
using Eod.TestRunner.Services;
using FluentAssertions;
using Xunit;

namespace Eod.TestRunner.Tests.Services;

public class ScenarioFactoryTests
{
    private readonly ScenarioFactory _factory;

    public ScenarioFactoryTests()
    {
        _factory = new ScenarioFactory();
    }

    [Fact]
    public void CreateExecutionScenario_GeneratesIdWithPrefix()
    {
        // Arrange
        var template = CreateTestScenario("template-1");

        // Act
        var execution = _factory.CreateExecutionScenario(template);

        // Assert
        execution.Id.Should().StartWith("template-1-");
        execution.Id.Should().MatchRegex(@"^template-1-\d{14}$");
    }
    
    [Fact]
    public async Task CreateExecutionScenario_GeneratesUniqueIdOverTime()
    {
        // Arrange
        var template = CreateTestScenario("template-1");

        // Act
        var execution1 = _factory.CreateExecutionScenario(template);
        await Task.Delay(1100); // Wait more than 1 second for different timestamp
        var execution2 = _factory.CreateExecutionScenario(template);

        // Assert
        execution1.Id.Should().NotBe(execution2.Id);
    }

    [Fact]
    public void CreateExecutionScenario_CopiesTemplateProperties()
    {
        // Arrange
        var template = CreateTestScenario("template", "Test Scenario", "Description");

        // Act
        var execution = _factory.CreateExecutionScenario(template);

        // Assert
        execution.Name.Should().Be(template.Name);
        execution.Description.Should().Be(template.Description);
        execution.Type.Should().Be(template.Type);
    }

    [Fact]
    public void CreateExecutionScenario_WithNoOverrides_UsesTemplateParameters()
    {
        // Arrange
        var template = CreateTestScenario("template");
        template.Parameters.TradeCount = 100;
        template.Parameters.TradesPerSecond = 10;

        // Act
        var execution = _factory.CreateExecutionScenario(template);

        // Assert
        execution.Parameters.TradeCount.Should().Be(100);
        execution.Parameters.TradesPerSecond.Should().Be(10);
    }

    [Fact]
    public void CreateExecutionScenario_WithOverrides_MergesParameters()
    {
        // Arrange
        var template = CreateTestScenario("template");
        template.Parameters.TradeCount = 100;
        template.Parameters.TradesPerSecond = 10;
        template.Parameters.TimeoutSeconds = 60;

        var overrides = new TestParameters
        {
            TradeCount = 200,
            // TradesPerSecond not overridden (default 0)
            TimeoutSeconds = 120
        };

        // Act
        var execution = _factory.CreateExecutionScenario(template, overrides);

        // Assert
        execution.Parameters.TradeCount.Should().Be(200); // Overridden
        execution.Parameters.TradesPerSecond.Should().Be(10); // From template
        execution.Parameters.TimeoutSeconds.Should().Be(120); // Overridden
    }

    [Fact]
    public void CreateExecutionScenario_WithSymbolOverrides_UsesOverriddenSymbols()
    {
        // Arrange
        var template = CreateTestScenario("template");
        template.Parameters.Symbols = ["AAPL", "MSFT"];

        var overrides = new TestParameters
        {
            Symbols = ["GOOGL", "AMZN", "META"]
        };

        // Act
        var execution = _factory.CreateExecutionScenario(template, overrides);

        // Assert
        execution.Parameters.Symbols.Should().BeEquivalentTo(["GOOGL", "AMZN", "META"]);
    }

    [Fact]
    public void CreateExecutionScenario_ThrowsOnNullTemplate()
    {
        // Act & Assert
        var act = () => _factory.CreateExecutionScenario(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CreateCustomScenario_GeneratesIdWithPrefix()
    {
        // Act
        var scenario = _factory.CreateCustomScenario("Test", TestType.Throughput, new TestParameters());

        // Assert
        scenario.Id.Should().StartWith("custom-");
        scenario.Id.Should().MatchRegex(@"^custom-\d{14}$");
    }
    
    [Fact]
    public async Task CreateCustomScenario_GeneratesUniqueIdOverTime()
    {
        // Act
        var scenario1 = _factory.CreateCustomScenario("Test", TestType.Throughput, new TestParameters());
        await Task.Delay(1100); // Wait more than 1 second for different timestamp
        var scenario2 = _factory.CreateCustomScenario("Test", TestType.Throughput, new TestParameters());

        // Assert
        scenario1.Id.Should().NotBe(scenario2.Id);
    }

    [Fact]
    public void CreateCustomScenario_SetsPropertiesCorrectly()
    {
        // Arrange
        var parameters = new TestParameters
        {
            TradeCount = 500,
            TradesPerSecond = 50
        };

        // Act
        var scenario = _factory.CreateCustomScenario("My Custom Test", TestType.Latency, parameters);

        // Assert
        scenario.Name.Should().Be("My Custom Test");
        scenario.Type.Should().Be(TestType.Latency);
        scenario.Description.Should().Contain("Latency");
        scenario.Parameters.TradeCount.Should().Be(500);
        scenario.Parameters.TradesPerSecond.Should().Be(50);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateCustomScenario_ThrowsOnInvalidName(string? name)
    {
        // Act & Assert
        var act = () => _factory.CreateCustomScenario(name!, TestType.Throughput, new TestParameters());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CreateCustomScenario_ThrowsOnNullParameters()
    {
        // Act & Assert
        var act = () => _factory.CreateCustomScenario("Test", TestType.Throughput, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    private static TestScenario CreateTestScenario(
        string id, 
        string name = "Test", 
        string description = "Test description")
    {
        return new TestScenario
        {
            Id = id,
            Name = name,
            Description = description,
            Type = TestType.Throughput,
            Parameters = new TestParameters
            {
                TimeoutSeconds = 60
            }
        };
    }
}
