using Eod.TestRunner.Abstractions;
using Eod.TestRunner.Controllers;
using Eod.TestRunner.Models;
using Eod.TestRunner.Models.Metrics;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Eod.TestRunner.Tests.Controllers;

public class ScenariosControllerTests
{
    private readonly Mock<IScenarioRepository> _repositoryMock;
    private readonly Mock<IScenarioFactory> _factoryMock;
    private readonly Mock<ITestExecutionService> _testServiceMock;
    private readonly Mock<ISystemMetricsAggregator> _metricsAggregatorMock;
    private readonly Mock<ILogger<ScenariosController>> _loggerMock;
    private readonly ScenariosController _controller;

    public ScenariosControllerTests()
    {
        _repositoryMock = new Mock<IScenarioRepository>();
        _factoryMock = new Mock<IScenarioFactory>();
        _testServiceMock = new Mock<ITestExecutionService>();
        _metricsAggregatorMock = new Mock<ISystemMetricsAggregator>();
        _loggerMock = new Mock<ILogger<ScenariosController>>();

        _controller = new ScenariosController(
            _repositoryMock.Object,
            _factoryMock.Object,
            _testServiceMock.Object,
            _metricsAggregatorMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public void GetScenarios_ReturnsAllScenarios()
    {
        // Arrange
        var scenarios = new List<TestScenario>
        {
            new() { Id = "1", Name = "Test 1", Description = "Desc 1", Type = TestType.HealthCheck, Parameters = new TestParameters() },
            new() { Id = "2", Name = "Test 2", Description = "Desc 2", Type = TestType.Throughput, Parameters = new TestParameters() }
        };
        _repositoryMock.Setup(r => r.GetAll()).Returns(scenarios);

        // Act
        var result = _controller.GetScenarios();

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedScenarios = okResult.Value.Should().BeAssignableTo<IEnumerable<TestScenario>>().Subject;
        returnedScenarios.Should().HaveCount(2);
    }

    [Fact]
    public void GetScenario_WithValidId_ReturnsScenario()
    {
        // Arrange
        var scenario = new TestScenario 
        { 
            Id = "test-1", 
            Name = "Test", 
            Description = "Test description",
            Type = TestType.HealthCheck,
            Parameters = new TestParameters() 
        };
        _repositoryMock.Setup(r => r.GetById("test-1")).Returns(scenario);

        // Act
        var result = _controller.GetScenario("test-1");

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedScenario = okResult.Value.Should().BeOfType<TestScenario>().Subject;
        returnedScenario.Id.Should().Be("test-1");
    }

    [Fact]
    public void GetScenario_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        _repositoryMock.Setup(r => r.GetById("invalid")).Returns((TestScenario?)null);

        // Act
        var result = _controller.GetScenario("invalid");

        // Assert
        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public void ExecuteScenario_WithValidId_ReturnsAccepted()
    {
        // Arrange
        var template = new TestScenario 
        { 
            Id = "test-1", 
            Name = "Test",
            Description = "Test description",
            Type = TestType.HealthCheck,
            Parameters = new TestParameters() 
        };
        var executionScenario = new TestScenario 
        { 
            Id = "test-1-123456", 
            Name = "Test",
            Description = "Test description",
            Type = TestType.HealthCheck,
            Parameters = new TestParameters() 
        };

        _repositoryMock.Setup(r => r.GetById("test-1")).Returns(template);
        _factoryMock.Setup(f => f.CreateExecutionScenario(template, null))
            .Returns(executionScenario);

        // Act
        var result = _controller.ExecuteScenario("test-1");

        // Assert
        result.Should().BeOfType<AcceptedResult>();
        _testServiceMock.Verify(s => s.ExecuteScenarioAsync(executionScenario), Times.Once);
    }

    [Fact]
    public void ExecuteScenario_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        _repositoryMock.Setup(r => r.GetById("invalid")).Returns((TestScenario?)null);

        // Act
        var result = _controller.ExecuteScenario("invalid");

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public void ExecuteScenario_WithOverrides_PassesOverridesToFactory()
    {
        // Arrange
        var template = new TestScenario 
        { 
            Id = "test-1", 
            Name = "Test",
            Description = "Test description",
            Type = TestType.Throughput,
            Parameters = new TestParameters { TradeCount = 100 } 
        };
        var overrides = new TestParameters { TradeCount = 200 };
        var executionScenario = new TestScenario 
        { 
            Id = "test-1-123456", 
            Name = "Test",
            Description = "Test description",
            Type = TestType.Throughput,
            Parameters = new TestParameters { TradeCount = 200 } 
        };

        _repositoryMock.Setup(r => r.GetById("test-1")).Returns(template);
        _factoryMock.Setup(f => f.CreateExecutionScenario(template, overrides))
            .Returns(executionScenario);

        // Act
        var result = _controller.ExecuteScenario("test-1", overrides);

        // Assert
        result.Should().BeOfType<AcceptedResult>();
        _factoryMock.Verify(f => f.CreateExecutionScenario(template, overrides), Times.Once);
    }

    [Fact]
    public void ExecuteCustomScenario_WithValidScenario_ReturnsAccepted()
    {
        // Arrange
        var inputScenario = new TestScenario 
        { 
            Name = "Custom Test",
            Description = "Custom description",
            Type = TestType.Throughput,
            Parameters = new TestParameters { TradeCount = 50 } 
        };
        var createdScenario = new TestScenario 
        { 
            Id = "custom-123456",
            Name = "Custom Test",
            Description = "Custom description",
            Type = TestType.Throughput,
            Parameters = new TestParameters { TradeCount = 50 } 
        };

        _factoryMock.Setup(f => f.CreateCustomScenario(
            inputScenario.Name, 
            inputScenario.Type, 
            inputScenario.Parameters))
            .Returns(createdScenario);

        // Act
        var result = _controller.ExecuteCustomScenario(inputScenario);

        // Assert
        result.Should().BeOfType<AcceptedResult>();
        _testServiceMock.Verify(s => s.ExecuteScenarioAsync(createdScenario), Times.Once);
    }

    [Fact]
    public void ExecuteCustomScenario_WithEmptyName_ReturnsBadRequest()
    {
        // Arrange
        var inputScenario = new TestScenario 
        { 
            Name = "",
            Description = "Description",
            Type = TestType.Throughput,
            Parameters = new TestParameters() 
        };

        // Act
        var result = _controller.ExecuteCustomScenario(inputScenario);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void GetResults_ReturnsAllResults()
    {
        // Arrange
        var results = new List<TestResult>
        {
            new() { ScenarioId = "1", ScenarioName = "Test 1", Status = TestStatus.Passed },
            new() { ScenarioId = "2", ScenarioName = "Test 2", Status = TestStatus.Failed }
        };
        _testServiceMock.Setup(s => s.GetAllResults()).Returns(results);

        // Act
        var result = _controller.GetResults();

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedResults = okResult.Value.Should().BeAssignableTo<IEnumerable<TestResult>>().Subject;
        returnedResults.Should().HaveCount(2);
    }

    [Fact]
    public void GetResult_WithValidId_ReturnsResult()
    {
        // Arrange
        var testResult = new TestResult 
        { 
            ScenarioId = "test-1", 
            ScenarioName = "Test",
            Status = TestStatus.Passed 
        };
        _testServiceMock.Setup(s => s.GetResult("test-1")).Returns(testResult);

        // Act
        var result = _controller.GetResult("test-1");

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedResult = okResult.Value.Should().BeOfType<TestResult>().Subject;
        returnedResult.ScenarioId.Should().Be("test-1");
    }

    [Fact]
    public void GetResult_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        _testServiceMock.Setup(s => s.GetResult("invalid")).Returns((TestResult?)null);

        // Act
        var result = _controller.GetResult("invalid");

        // Assert
        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public void CancelTest_CallsServiceCancel()
    {
        // Act
        var result = _controller.CancelTest("test-1");

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        _testServiceMock.Verify(s => s.CancelTest("test-1"), Times.Once);
    }

    [Fact]
    public async Task GetSystemMetrics_ReturnsAggregatedMetrics()
    {
        // Arrange
        var systemMetrics = new SystemMetrics
        {
            Ingestion = new IngestionMetrics { TradesIngested = 1000, Status = "up" },
            Kafka = new KafkaMetrics { MessagesInTopic = 5000, Status = "up" },
            FlashPnl = new FlashPnlMetrics { TradesProcessed = 900, Status = "up" },
            Regulatory = new RegulatoryMetrics { TradesInserted = 800, Status = "up" },
            Redis = new RedisMetrics { ConnectedClients = 5, Status = "up" },
            SqlServer = new SqlServerMetrics { TotalTrades = 10000, Status = "up" }
        };
        
        _metricsAggregatorMock.Setup(m => m.CollectAllMetricsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(systemMetrics);

        // Act
        var result = await _controller.GetSystemMetrics(CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().Be(systemMetrics);
    }
}
