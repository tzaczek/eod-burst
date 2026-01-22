using Eod.TestRunner.Models;

namespace Eod.TestRunner.Abstractions;

/// <summary>
/// Repository pattern for managing test scenarios.
/// Follows Single Responsibility Principle - only handles scenario persistence/retrieval.
/// </summary>
public interface IScenarioRepository
{
    /// <summary>
    /// Gets all available test scenarios.
    /// </summary>
    IReadOnlyList<TestScenario> GetAll();
    
    /// <summary>
    /// Gets a scenario by its identifier.
    /// </summary>
    TestScenario? GetById(string id);
    
    /// <summary>
    /// Checks if a scenario exists.
    /// </summary>
    bool Exists(string id);
}

/// <summary>
/// Factory pattern for creating test scenarios.
/// Follows Open/Closed Principle - can add new scenario types without modifying existing code.
/// </summary>
public interface IScenarioFactory
{
    /// <summary>
    /// Creates a new execution scenario from a template with optional parameter overrides.
    /// </summary>
    TestScenario CreateExecutionScenario(TestScenario template, TestParameters? overrideParams = null);
    
    /// <summary>
    /// Creates a custom scenario with user-defined parameters.
    /// </summary>
    TestScenario CreateCustomScenario(string name, TestType type, TestParameters parameters);
}
