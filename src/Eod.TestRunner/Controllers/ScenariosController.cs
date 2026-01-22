using Eod.TestRunner.Abstractions;
using Eod.TestRunner.Models;
using Microsoft.AspNetCore.Mvc;

namespace Eod.TestRunner.Controllers;

/// <summary>
/// API controller for managing test scenarios.
/// Follows SRP - delegates to specialized services.
/// Follows DIP - depends on abstractions, not concrete implementations.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ScenariosController : ControllerBase
{
    private readonly IScenarioRepository _scenarioRepository;
    private readonly IScenarioFactory _scenarioFactory;
    private readonly ITestExecutionService _testService;
    private readonly ISystemMetricsAggregator _metricsAggregator;
    private readonly ILogger<ScenariosController> _logger;

    public ScenariosController(
        IScenarioRepository scenarioRepository,
        IScenarioFactory scenarioFactory,
        ITestExecutionService testService,
        ISystemMetricsAggregator metricsAggregator,
        ILogger<ScenariosController> logger)
    {
        _scenarioRepository = scenarioRepository;
        _scenarioFactory = scenarioFactory;
        _testService = testService;
        _metricsAggregator = metricsAggregator;
        _logger = logger;
    }

    /// <summary>
    /// Gets all predefined test scenarios.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<TestScenario>), StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<TestScenario>> GetScenarios()
    {
        return Ok(_scenarioRepository.GetAll());
    }

    /// <summary>
    /// Gets a specific scenario by ID.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(TestScenario), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<TestScenario> GetScenario(string id)
    {
        var scenario = _scenarioRepository.GetById(id);
        if (scenario == null)
            return NotFound();
        
        return Ok(scenario);
    }

    /// <summary>
    /// Executes a predefined scenario by ID.
    /// </summary>
    [HttpPost("{id}/execute")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult ExecuteScenario(
        string id, 
        [FromBody] TestParameters? overrideParams = null)
    {
        var template = _scenarioRepository.GetById(id);
        if (template == null)
            return NotFound($"Scenario '{id}' not found");

        var executionScenario = _scenarioFactory.CreateExecutionScenario(template, overrideParams);

        _logger.LogInformation("Executing scenario {Id}: {Name}", 
            executionScenario.Id, executionScenario.Name);
        
        // Fire and forget - results come via SignalR
        _ = _testService.ExecuteScenarioAsync(executionScenario);
        
        return Accepted(new { scenarioId = executionScenario.Id, message = "Test started" });
    }

    /// <summary>
    /// Executes a custom scenario with user-defined parameters.
    /// </summary>
    [HttpPost("custom")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult ExecuteCustomScenario([FromBody] TestScenario scenario)
    {
        if (string.IsNullOrEmpty(scenario.Name))
            return BadRequest("Scenario name is required");

        var executionScenario = _scenarioFactory.CreateCustomScenario(
            scenario.Name, scenario.Type, scenario.Parameters);
        
        _logger.LogInformation("Executing custom scenario {Id}: {Name}", 
            executionScenario.Id, executionScenario.Name);
        
        _ = _testService.ExecuteScenarioAsync(executionScenario);
        
        return Accepted(new { scenarioId = executionScenario.Id, message = "Custom test started" });
    }

    /// <summary>
    /// Gets all test results.
    /// </summary>
    [HttpGet("results")]
    [ProducesResponseType(typeof(IEnumerable<TestResult>), StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<TestResult>> GetResults()
    {
        return Ok(_testService.GetAllResults());
    }

    /// <summary>
    /// Gets a specific test result.
    /// </summary>
    [HttpGet("results/{scenarioId}")]
    [ProducesResponseType(typeof(TestResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<TestResult> GetResult(string scenarioId)
    {
        var result = _testService.GetResult(scenarioId);
        if (result == null)
            return NotFound();
        
        return Ok(result);
    }

    /// <summary>
    /// Cancels a running test.
    /// </summary>
    [HttpPost("results/{scenarioId}/cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult CancelTest(string scenarioId)
    {
        _testService.CancelTest(scenarioId);
        return Ok(new { message = "Cancellation requested" });
    }

    /// <summary>
    /// Gets live system metrics for the architecture diagram.
    /// </summary>
    [HttpGet("metrics")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> GetSystemMetrics(CancellationToken cancellationToken)
    {
        var metrics = await _metricsAggregator.CollectAllMetricsAsync(cancellationToken);
        return Ok(metrics);
    }
}
