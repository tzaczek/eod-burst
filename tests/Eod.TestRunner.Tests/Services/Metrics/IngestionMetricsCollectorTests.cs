using System.Net;
using Eod.Shared.Resilience;
using Eod.TestRunner.Models.Metrics;
using Eod.TestRunner.Services.Metrics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace Eod.TestRunner.Tests.Services.Metrics;

public class IngestionMetricsCollectorTests
{
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ICircuitBreakerFactory> _circuitBreakerFactoryMock;
    private readonly Mock<ILogger<IngestionMetricsCollector>> _loggerMock;
    private readonly Mock<HttpMessageHandler> _handlerMock;
    
    public IngestionMetricsCollectorTests()
    {
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _circuitBreakerFactoryMock = new Mock<ICircuitBreakerFactory>();
        _loggerMock = new Mock<ILogger<IngestionMetricsCollector>>();
        _handlerMock = new Mock<HttpMessageHandler>();
        
        // Setup default circuit breaker
        var circuitBreaker = new CircuitBreaker(
            CircuitBreakerOptions.HighAvailability with { Name = "IngestionMetrics" });
        _circuitBreakerFactoryMock
            .Setup(f => f.GetOrCreate(It.IsAny<string>(), It.IsAny<CircuitBreakerOptions>()))
            .Returns(circuitBreaker);
    }

    [Fact]
    public async Task CollectAsync_WhenServiceIsHealthy_ReturnsMetrics()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK, @"
eod_trades_ingested_trades_total{symbol=""AAPL""} 100
eod_trades_ingested_trades_total{symbol=""MSFT""} 200
");
        var collector = CreateCollector();

        // Act
        var result = await collector.CollectAsync();

        // Assert
        result.Status.Should().Be("up");
        result.TradesIngested.Should().Be(300);
    }

    [Fact]
    public async Task CollectAsync_WhenServiceIsUnhealthy_ReturnsDownStatus()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.ServiceUnavailable, "");
        var collector = CreateCollector();

        // Act
        var result = await collector.CollectAsync();

        // Assert
        result.Status.Should().Be("down");
        result.TradesIngested.Should().Be(0);
    }

    [Fact]
    public async Task CollectAsync_WhenHttpClientThrows_ReturnsDownStatus()
    {
        // Arrange
        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var httpClient = new HttpClient(_handlerMock.Object);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        
        var collector = CreateCollector();

        // Act
        var result = await collector.CollectAsync();

        // Assert
        result.Status.Should().Be("down");
    }

    [Fact]
    public async Task CollectAsync_WhenCircuitBreakerOpen_ReturnsCircuitOpenStatus()
    {
        // Arrange
        var openCircuitBreaker = new Mock<ICircuitBreaker>();
        openCircuitBreaker.Setup(cb => cb.State).Returns(CircuitBreakerState.Open);
        
        _circuitBreakerFactoryMock
            .Setup(f => f.GetOrCreate(It.IsAny<string>(), It.IsAny<CircuitBreakerOptions>()))
            .Returns(openCircuitBreaker.Object);
        
        SetupHttpResponse(HttpStatusCode.OK, "");
        var collector = CreateCollector();

        // Act
        var result = await collector.CollectAsync();

        // Assert
        result.Status.Should().Be("circuit-open");
    }

    [Fact]
    public async Task IsHealthyAsync_WhenServiceResponds200_ReturnsTrue()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK, "OK");
        var collector = CreateCollector();

        // Act
        var result = await collector.IsHealthyAsync();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsHealthyAsync_WhenServiceResponds500_ReturnsFalse()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.InternalServerError, "Error");
        var collector = CreateCollector();

        // Act
        var result = await collector.IsHealthyAsync();

        // Assert
        result.Should().BeFalse();
    }

    private IngestionMetricsCollector CreateCollector()
    {
        return new IngestionMetricsCollector(
            _httpClientFactoryMock.Object,
            _circuitBreakerFactoryMock.Object,
            _loggerMock.Object);
    }

    private void SetupHttpResponse(HttpStatusCode statusCode, string content)
    {
        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(content)
            });

        var httpClient = new HttpClient(_handlerMock.Object);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
    }
}
