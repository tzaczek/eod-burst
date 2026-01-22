using Eod.TestRunner.Models;

namespace Eod.TestRunner.Abstractions;

/// <summary>
/// Strategy pattern interface for executing different types of tests.
/// Follows Open/Closed Principle - new test types can be added without modifying existing code.
/// </summary>
public interface ITestExecutor
{
    /// <summary>
    /// Gets the test type this executor handles.
    /// </summary>
    TestType TestType { get; }
    
    /// <summary>
    /// Executes the test scenario asynchronously.
    /// </summary>
    /// <param name="scenario">The scenario to execute</param>
    /// <param name="progressCallback">Callback for reporting progress</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The test result</returns>
    Task<TestResult> ExecuteAsync(
        TestScenario scenario,
        Action<TestProgress> progressCallback,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Observer pattern interface for receiving test execution notifications.
/// </summary>
public interface ITestObserver
{
    /// <summary>
    /// Called when test progress is updated.
    /// </summary>
    Task OnProgressAsync(TestProgress progress);
    
    /// <summary>
    /// Called when a test completes.
    /// </summary>
    Task OnCompletedAsync(TestResult result);
}

/// <summary>
/// Service interface for managing test execution.
/// Follows Dependency Inversion Principle - high-level modules depend on abstractions.
/// </summary>
public interface ITestExecutionService
{
    /// <summary>
    /// Executes a scenario asynchronously and notifies observers.
    /// </summary>
    Task<TestResult> ExecuteScenarioAsync(TestScenario scenario);
    
    /// <summary>
    /// Gets all test results.
    /// </summary>
    IReadOnlyList<TestResult> GetAllResults();
    
    /// <summary>
    /// Gets a specific test result.
    /// </summary>
    TestResult? GetResult(string scenarioId);
    
    /// <summary>
    /// Cancels a running test.
    /// </summary>
    void CancelTest(string scenarioId);
    
    /// <summary>
    /// Registers an observer for test events.
    /// </summary>
    void RegisterObserver(ITestObserver observer);
    
    /// <summary>
    /// Unregisters an observer.
    /// </summary>
    void UnregisterObserver(ITestObserver observer);
}
