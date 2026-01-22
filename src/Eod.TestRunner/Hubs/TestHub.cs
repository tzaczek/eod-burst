using Eod.TestRunner.Models;
using Eod.TestRunner.Services;
using Microsoft.AspNetCore.SignalR;

namespace Eod.TestRunner.Hubs;

/// <summary>
/// SignalR hub for real-time test updates.
/// </summary>
public class TestHub : Hub
{
    private readonly TestExecutionService _testService;
    private readonly ILogger<TestHub> _logger;

    public TestHub(TestExecutionService testService, ILogger<TestHub> logger)
    {
        _testService = testService;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        
        // Send current results to newly connected client
        var results = _testService.GetAllResults();
        foreach (var result in results)
        {
            await Clients.Caller.SendAsync("TestCompleted", result);
        }
        
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Allows clients to subscribe to specific test scenarios.
    /// </summary>
    public async Task SubscribeToTest(string scenarioId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, scenarioId);
        
        var result = _testService.GetResult(scenarioId);
        if (result != null)
        {
            await Clients.Caller.SendAsync("TestCompleted", result);
        }
    }

    /// <summary>
    /// Allows clients to unsubscribe from specific test scenarios.
    /// </summary>
    public async Task UnsubscribeFromTest(string scenarioId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, scenarioId);
    }

    /// <summary>
    /// Cancels a running test.
    /// </summary>
    public void CancelTest(string scenarioId)
    {
        _testService.CancelTest(scenarioId);
    }
}
