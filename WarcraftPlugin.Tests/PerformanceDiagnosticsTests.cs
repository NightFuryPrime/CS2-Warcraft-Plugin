using System;
using System.Diagnostics;
using WarcraftPlugin.Core;
using WarcraftPlugin.Diagnostics;
using Xunit;

namespace WarcraftPlugin.Tests;

public class PerformanceDiagnosticsTests
{
    [Fact]
    public void SampleBufferCalculatesCountAverageP95AndMaximum()
    {
        var buffer = new PerformanceSampleBuffer(samplesPerMetric: 8);
        buffer.Record(PerformanceMetric.EffectTick, 10);
        buffer.Record(PerformanceMetric.EffectTick, 20);
        buffer.Record(PerformanceMetric.EffectTick, 30);
        buffer.Record(PerformanceMetric.EffectTick, 40);
        buffer.Record(PerformanceMetric.EffectTick, 50);

        var result = buffer.GetStatistics(PerformanceMetric.EffectTick);
        var tickToMilliseconds = 1000d / Stopwatch.Frequency;

        Assert.Equal(5, result.Count);
        Assert.Equal(30d * tickToMilliseconds, result.AverageMilliseconds, precision: 10);
        Assert.Equal(50d * tickToMilliseconds, result.P95Milliseconds, precision: 10);
        Assert.Equal(50d * tickToMilliseconds, result.MaxMilliseconds, precision: 10);
    }

    [Fact]
    public void SampleBufferKeepsAggregateStatisticsWhenTheP95RingIsFull()
    {
        var buffer = new PerformanceSampleBuffer(samplesPerMetric: 2);
        buffer.Record(PerformanceMetric.DroneTick, 10);
        buffer.Record(PerformanceMetric.DroneTick, 20);
        buffer.Record(PerformanceMetric.DroneTick, 100);

        var result = buffer.GetStatistics(PerformanceMetric.DroneTick);
        var tickToMilliseconds = 1000d / Stopwatch.Frequency;

        Assert.Equal(3, result.Count);
        Assert.Equal(130d * tickToMilliseconds / 3d, result.AverageMilliseconds, precision: 10);
        Assert.Equal(100d * tickToMilliseconds, result.MaxMilliseconds, precision: 10);
        Assert.Equal(20d * tickToMilliseconds, result.P95Milliseconds, precision: 10);
    }

    [Fact]
    public void QueuePressureRequiresSustainedDepthAndRateLimitsWarnings()
    {
        var monitor = new QueuePressureMonitor(queueThreshold: 5, requiredConsecutiveSamples: 2, repeatIntervalSamples: 3);

        Assert.False(monitor.Observe(5));
        Assert.True(monitor.Observe(5));
        Assert.False(monitor.Observe(5));
        Assert.False(monitor.Observe(5));
        Assert.True(monitor.Observe(5));
        Assert.False(monitor.Observe(4));
        Assert.False(monitor.Observe(5));
        Assert.True(monitor.Observe(5));
    }
}
