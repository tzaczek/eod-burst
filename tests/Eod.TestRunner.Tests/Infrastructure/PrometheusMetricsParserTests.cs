using Eod.TestRunner.Infrastructure;
using FluentAssertions;
using Xunit;

namespace Eod.TestRunner.Tests.Infrastructure;

public class PrometheusMetricsParserTests
{
    private const string SampleMetrics = @"
# HELP eod_trades_ingested_trades_total Total trades ingested
# TYPE eod_trades_ingested_trades_total counter
eod_trades_ingested_trades_total{otel_scope_name=""Eod.Burst"",symbol=""AAPL""} 100
eod_trades_ingested_trades_total{otel_scope_name=""Eod.Burst"",symbol=""MSFT""} 200
eod_trades_ingested_trades_total{otel_scope_name=""Eod.Burst"",symbol=""GOOGL""} 150
# HELP redis_connected_clients Number of connected clients
# TYPE redis_connected_clients gauge
redis_connected_clients 5
# HELP kafka_consumergroup_lag Consumer group lag
# TYPE kafka_consumergroup_lag gauge
kafka_consumergroup_lag{consumergroup=""flash-pnl-group"",topic=""trades.raw"",partition=""0""} 100
kafka_consumergroup_lag{consumergroup=""regulatory-group"",topic=""trades.raw"",partition=""0""} 250
";

    [Fact]
    public void SumMetric_WithLabeledMetrics_SumsAllValues()
    {
        // Act
        var result = PrometheusMetricsParser.SumMetric(SampleMetrics, "eod_trades_ingested_trades_total");

        // Assert
        result.Should().Be(450); // 100 + 200 + 150
    }

    [Fact]
    public void SumMetric_WithNonExistentMetric_ReturnsZero()
    {
        // Act
        var result = PrometheusMetricsParser.SumMetric(SampleMetrics, "non_existent_metric");

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void SumMetric_WithEmptyContent_ReturnsZero()
    {
        // Act
        var result = PrometheusMetricsParser.SumMetric("", "any_metric");

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void SumMetric_ThrowsOnNullContent()
    {
        // Act & Assert
        var act = () => PrometheusMetricsParser.SumMetric(null!, "metric");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SumMetric_ThrowsOnNullOrWhiteSpaceMetricName()
    {
        // Act & Assert
        var actNull = () => PrometheusMetricsParser.SumMetric(SampleMetrics, null!);
        var actEmpty = () => PrometheusMetricsParser.SumMetric(SampleMetrics, "");
        var actWhitespace = () => PrometheusMetricsParser.SumMetric(SampleMetrics, "   ");

        actNull.Should().Throw<ArgumentException>();
        actEmpty.Should().Throw<ArgumentException>();
        actWhitespace.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetMetricValue_WithUnlabeledMetric_ReturnsValue()
    {
        // Act
        var result = PrometheusMetricsParser.GetMetricValue(SampleMetrics, "redis_connected_clients");

        // Assert
        result.Should().Be(5);
    }

    [Fact]
    public void GetMetricValue_WithLabeledMetric_ReturnsFirstMatch()
    {
        // Act
        var result = PrometheusMetricsParser.GetMetricValue(SampleMetrics, "kafka_consumergroup_lag");

        // Assert
        result.Should().Be(100); // First match
    }

    [Fact]
    public void GetMetricValue_WithNonExistentMetric_ReturnsZero()
    {
        // Act
        var result = PrometheusMetricsParser.GetMetricValue(SampleMetrics, "non_existent_metric");

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void GetMetricValues_ReturnsAllLabelCombinations()
    {
        // Act
        var result = PrometheusMetricsParser.GetMetricValues(SampleMetrics, "eod_trades_ingested_trades_total");

        // Assert
        result.Should().HaveCount(3);
        result.Values.Should().Contain(100);
        result.Values.Should().Contain(200);
        result.Values.Should().Contain(150);
    }

    [Fact]
    public void GetMetricValues_WithNoLabels_ReturnsEmptyKeyEntry()
    {
        // Act
        var result = PrometheusMetricsParser.GetMetricValues(SampleMetrics, "redis_connected_clients");

        // Assert
        result.Should().HaveCount(1);
        result.Should().ContainKey("");
        result[""].Should().Be(5);
    }

    [Fact]
    public void GetMetricValues_WithNonExistentMetric_ReturnsEmptyDictionary()
    {
        // Act
        var result = PrometheusMetricsParser.GetMetricValues(SampleMetrics, "non_existent_metric");

        // Assert
        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData("metric_name 123.456", "metric_name", 123)]
    [InlineData("metric_name{label=\"value\"} 999", "metric_name", 999)]
    [InlineData("metric_with_underscore_name 42", "metric_with_underscore_name", 42)]
    public void SumMetric_HandlesVariousFormats(string content, string metricName, long expected)
    {
        // Act
        var result = PrometheusMetricsParser.SumMetric(content, metricName);

        // Assert
        result.Should().Be(expected);
    }
}
