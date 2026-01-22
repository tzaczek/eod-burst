using Eod.TestRunner.Models;
using Eod.TestRunner.Services;
using FluentAssertions;
using Xunit;

namespace Eod.TestRunner.Tests.Services;

public class ScenarioRepositoryTests
{
    private readonly ScenarioRepository _repository;

    public ScenarioRepositoryTests()
    {
        _repository = new ScenarioRepository();
    }

    [Fact]
    public void GetAll_ReturnsPredefinedScenarios()
    {
        // Act
        var scenarios = _repository.GetAll();

        // Assert
        scenarios.Should().NotBeEmpty();
        scenarios.Should().HaveCountGreaterThan(5);
    }

    [Fact]
    public void GetAll_ReturnsImmutableList()
    {
        // Act
        var scenarios1 = _repository.GetAll();
        var scenarios2 = _repository.GetAll();

        // Assert
        scenarios1.Should().BeSameAs(scenarios2);
    }

    [Theory]
    [InlineData("health-check")]
    [InlineData("throughput-basic")]
    [InlineData("e2e-small")]
    [InlineData("burst-5x")]
    public void GetById_WithValidId_ReturnsScenario(string scenarioId)
    {
        // Act
        var scenario = _repository.GetById(scenarioId);

        // Assert
        scenario.Should().NotBeNull();
        scenario!.Id.Should().Be(scenarioId);
    }

    [Fact]
    public void GetById_WithInvalidId_ReturnsNull()
    {
        // Act
        var scenario = _repository.GetById("non-existent-scenario");

        // Assert
        scenario.Should().BeNull();
    }

    [Fact]
    public void GetById_ThrowsOnNullOrWhitespace()
    {
        // Act & Assert
        var actNull = () => _repository.GetById(null!);
        var actEmpty = () => _repository.GetById("");
        var actWhitespace = () => _repository.GetById("   ");

        actNull.Should().Throw<ArgumentException>();
        actEmpty.Should().Throw<ArgumentException>();
        actWhitespace.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("health-check", true)]
    [InlineData("throughput-basic", true)]
    [InlineData("non-existent", false)]
    public void Exists_ReturnsCorrectResult(string scenarioId, bool expectedExists)
    {
        // Act
        var exists = _repository.Exists(scenarioId);

        // Assert
        exists.Should().Be(expectedExists);
    }

    [Fact]
    public void Exists_ThrowsOnNullOrWhitespace()
    {
        // Act & Assert
        var actNull = () => _repository.Exists(null!);
        var actEmpty = () => _repository.Exists("");

        actNull.Should().Throw<ArgumentException>();
        actEmpty.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AllScenarios_HaveValidParameters()
    {
        // Act
        var scenarios = _repository.GetAll();

        // Assert
        foreach (var scenario in scenarios)
        {
            scenario.Id.Should().NotBeNullOrEmpty();
            scenario.Name.Should().NotBeNullOrEmpty();
            scenario.Description.Should().NotBeNullOrEmpty();
            scenario.Parameters.Should().NotBeNull();
            scenario.Parameters.TimeoutSeconds.Should().BePositive();
        }
    }

    [Fact]
    public void AllScenarios_HaveUniqueIds()
    {
        // Act
        var scenarios = _repository.GetAll();
        var ids = scenarios.Select(s => s.Id).ToList();

        // Assert
        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void HealthCheckScenario_HasCorrectType()
    {
        // Act
        var scenario = _repository.GetById("health-check");

        // Assert
        scenario.Should().NotBeNull();
        scenario!.Type.Should().Be(TestType.HealthCheck);
    }

    [Fact]
    public void EndToEndScenarios_HaveTradeCountAndSymbols()
    {
        // Act
        var scenarios = _repository.GetAll()
            .Where(s => s.Type == TestType.EndToEnd)
            .ToList();

        // Assert
        scenarios.Should().NotBeEmpty();
        foreach (var scenario in scenarios)
        {
            scenario.Parameters.TradeCount.Should().BePositive();
            scenario.Parameters.Symbols.Should().NotBeEmpty();
            scenario.Parameters.TraderIds.Should().NotBeEmpty();
        }
    }
}
