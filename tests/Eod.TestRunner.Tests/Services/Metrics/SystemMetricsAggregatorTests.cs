using Eod.TestRunner.Abstractions;
using Eod.TestRunner.Models.Metrics;
using Eod.TestRunner.Services.Metrics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Eod.TestRunner.Tests.Services.Metrics;

public class SystemMetricsAggregatorTests
{
    private readonly Mock<IMetricsCollector<IngestionMetrics>> _ingestionCollectorMock;
    private readonly Mock<IMetricsCollector<KafkaMetrics>> _kafkaCollectorMock;
    private readonly Mock<IMetricsCollector<FlashPnlMetrics>> _flashPnlCollectorMock;
    private readonly Mock<IMetricsCollector<RegulatoryMetrics>> _regulatoryCollectorMock;
    private readonly Mock<IMetricsCollector<RedisMetrics>> _redisCollectorMock;
    private readonly Mock<IMetricsCollector<SqlServerMetrics>> _sqlServerCollectorMock;
    private readonly Mock<ILogger<SystemMetricsAggregator>> _loggerMock;

    public SystemMetricsAggregatorTests()
    {
        _ingestionCollectorMock = new Mock<IMetricsCollector<IngestionMetrics>>();
        _kafkaCollectorMock = new Mock<IMetricsCollector<KafkaMetrics>>();
        _flashPnlCollectorMock = new Mock<IMetricsCollector<FlashPnlMetrics>>();
        _regulatoryCollectorMock = new Mock<IMetricsCollector<RegulatoryMetrics>>();
        _redisCollectorMock = new Mock<IMetricsCollector<RedisMetrics>>();
        _sqlServerCollectorMock = new Mock<IMetricsCollector<SqlServerMetrics>>();
        _loggerMock = new Mock<ILogger<SystemMetricsAggregator>>();
    }

    [Fact]
    public async Task CollectAllMetricsAsync_AggregatesAllComponentMetrics()
    {
        // Arrange
        var ingestionMetrics = new IngestionMetrics { TradesIngested = 1000, Status = "up" };
        var kafkaMetrics = new KafkaMetrics { MessagesInTopic = 5000, ConsumerLag = 100, Status = "up" };
        var flashPnlMetrics = new FlashPnlMetrics { TradesProcessed = 900, PositionsInRedis = 50, Status = "up" };
        var regulatoryMetrics = new RegulatoryMetrics { TradesInserted = 800, Status = "up" };
        var redisMetrics = new RedisMetrics { ConnectedClients = 5, KeysCount = 100, Status = "up" };
        var sqlServerMetrics = new SqlServerMetrics { TotalTrades = 10000, Status = "up" };

        _ingestionCollectorMock.Setup(c => c.CollectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ingestionMetrics);
        _kafkaCollectorMock.Setup(c => c.CollectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(kafkaMetrics);
        _flashPnlCollectorMock.Setup(c => c.CollectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(flashPnlMetrics);
        _regulatoryCollectorMock.Setup(c => c.CollectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(regulatoryMetrics);
        _redisCollectorMock.Setup(c => c.CollectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(redisMetrics);
        _sqlServerCollectorMock.Setup(c => c.CollectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(sqlServerMetrics);

        var aggregator = CreateAggregator();

        // Act
        var result = await aggregator.CollectAllMetricsAsync();

        // Assert
        result.Ingestion.Should().Be(ingestionMetrics);
        result.Kafka.Should().Be(kafkaMetrics);
        result.FlashPnl.Should().Be(flashPnlMetrics);
        result.Regulatory.Should().Be(regulatoryMetrics);
        result.Redis.Should().Be(redisMetrics);
        result.SqlServer.Should().Be(sqlServerMetrics);
    }

    [Fact]
    public async Task CollectAllMetricsAsync_CollectsAllMetricsInParallel()
    {
        // Arrange
        var tcs = new TaskCompletionSource<IngestionMetrics>();
        var collectionStarted = new TaskCompletionSource<bool>();

        _ingestionCollectorMock.Setup(c => c.CollectAsync(It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                collectionStarted.SetResult(true);
                return await tcs.Task;
            });
        
        // Other collectors return immediately
        SetupQuickReturningCollectors();

        var aggregator = CreateAggregator();

        // Act
        var collectTask = aggregator.CollectAllMetricsAsync();
        
        // Wait for ingestion collector to start
        await collectionStarted.Task;
        
        // Complete the slow collector
        tcs.SetResult(new IngestionMetrics { Status = "up" });
        
        var result = await collectTask;

        // Assert
        result.Should().NotBeNull();
        result.Ingestion.Status.Should().Be("up");
    }

    [Fact]
    public async Task CollectAllMetricsAsync_RespecsCancellationToken()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _ingestionCollectorMock.Setup(c => c.CollectAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var aggregator = CreateAggregator();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => aggregator.CollectAllMetricsAsync(cts.Token));
    }

    [Fact]
    public async Task CollectAllMetricsAsync_WhenCollectorFails_PropagatesException()
    {
        // Arrange
        _ingestionCollectorMock.Setup(c => c.CollectAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test error"));
        
        SetupQuickReturningCollectors();

        var aggregator = CreateAggregator();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => aggregator.CollectAllMetricsAsync());
    }

    private SystemMetricsAggregator CreateAggregator()
    {
        return new SystemMetricsAggregator(
            _ingestionCollectorMock.Object,
            _kafkaCollectorMock.Object,
            _flashPnlCollectorMock.Object,
            _regulatoryCollectorMock.Object,
            _redisCollectorMock.Object,
            _sqlServerCollectorMock.Object,
            _loggerMock.Object);
    }

    private void SetupQuickReturningCollectors()
    {
        _kafkaCollectorMock.Setup(c => c.CollectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KafkaMetrics { Status = "up" });
        _flashPnlCollectorMock.Setup(c => c.CollectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FlashPnlMetrics { Status = "up" });
        _regulatoryCollectorMock.Setup(c => c.CollectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegulatoryMetrics { Status = "up" });
        _redisCollectorMock.Setup(c => c.CollectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RedisMetrics { Status = "up" });
        _sqlServerCollectorMock.Setup(c => c.CollectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SqlServerMetrics { Status = "up" });
    }
}
