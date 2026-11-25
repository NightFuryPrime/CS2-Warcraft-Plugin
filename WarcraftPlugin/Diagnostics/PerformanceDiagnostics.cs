using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using WarcraftPlugin.Classes;
using WarcraftPlugin.Core;

namespace WarcraftPlugin.Diagnostics;

internal enum PerformanceMetric
{
    EffectTick,
    DroneTick,
    BotUltimateTick,
    InvisibilityTransmit,
    DynamicEvent,
    PlayerSpawnEvent,
    PlayerHurtEvent,
    PlayerDeathEvent,
    RoundStartEvent,
    RoundEndEvent,
    PlayerDisconnectEvent
}

internal readonly struct PerformanceScope : IDisposable
{
    private readonly PerformanceDiagnostics? _owner;
    private readonly PerformanceMetric _metric;
    private readonly long _startedAt;

    internal PerformanceScope(PerformanceDiagnostics owner, PerformanceMetric metric)
    {
        _owner = owner;
        _metric = metric;
        _startedAt = Stopwatch.GetTimestamp();
    }

    public void Dispose()
    {
        if (_owner != null)
            _owner.Record(_metric, Stopwatch.GetTimestamp() - _startedAt);
    }
}

internal sealed class PerformanceDiagnostics : IDisposable
{
    private const int MinimumCaptureSeconds = 10;
    private const int MaximumCaptureSeconds = 300;
    private const int SamplesPerMetric = 8192;

    private readonly WarcraftPlugin _plugin;
    private PerformanceSampleBuffer? _samples;
    private Timer? _completionTimer;
    private CCSPlayerController? _requester;
    private bool _capturing;

    internal bool IsCapturing => _capturing;

    internal PerformanceDiagnostics(WarcraftPlugin plugin)
    {
        _plugin = plugin;
    }

    internal PerformanceScope Measure(PerformanceMetric metric)
    {
        return _capturing ? new PerformanceScope(this, metric) : default;
    }

    internal bool Start(int requestedSeconds, CCSPlayerController requester, out string message)
    {
        if (_capturing)
        {
            message = "A Warcraft performance capture is already running.";
            return false;
        }

        var seconds = Math.Clamp(requestedSeconds, MinimumCaptureSeconds, MaximumCaptureSeconds);
        _samples = new PerformanceSampleBuffer(SamplesPerMetric);
        _requester = requester;
        _capturing = true;
        _completionTimer?.Kill();
        _completionTimer = _plugin.AddTimer(seconds, () => Complete("timer"));
        message = $"Warcraft performance capture started for {seconds} seconds.";
        PersistentLogger.Info(nameof(PerformanceDiagnostics), message, mirrorConsole: false);
        return true;
    }

    internal void Record(PerformanceMetric metric, long elapsedStopwatchTicks)
    {
        if (_capturing)
            _samples?.Record(metric, elapsedStopwatchTicks);
    }

    private void Complete(string reason)
    {
        if (!_capturing)
            return;

        _capturing = false;
        _completionTimer = null;
        var report = BuildReport(reason);
        _samples = null;
        PersistentLogger.Info(nameof(PerformanceDiagnostics), report, mirrorConsole: true);

        var requester = _requester;
        _requester = null;
        if (requester?.IsValid == true)
        {
            requester.PrintToChat(" [Warcraft] Performance capture complete. Full report was written to the Warcraft log.");
            requester.PrintToConsole(report);
        }
    }

    private string BuildReport(string reason)
    {
        var players = PlayerCache.GetPlayers().Count;
        var effects = _plugin.EffectManager?.ActiveEffectCount ?? 0;
        var queueDepth = _plugin.DatabasePendingQueueDepth;
        var dirtyCount = _plugin.DatabaseDirtyCount;
        var builder = new StringBuilder();
        builder.AppendLine($"Performance capture complete ({reason}). players={players}, effects={effects}, drones={Tinker.ActiveDroneCount}, invisible={WarcraftInvisibilityManager.ActiveCount}, dbQueue={queueDepth}, dirtyPlayers={dirtyCount}");

        foreach (var metric in Enum.GetValues<PerformanceMetric>())
        {
            var statistics = _samples?.GetStatistics(metric) ?? default;
            if (statistics.Count == 0)
                continue;

            builder.Append(metric)
                .Append(": count=").Append(statistics.Count)
                .Append(", avg=").Append(FormatMilliseconds(statistics.AverageMilliseconds))
                .Append("ms, p95=").Append(FormatMilliseconds(statistics.P95Milliseconds))
                .Append("ms, max=").Append(FormatMilliseconds(statistics.MaxMilliseconds))
                .AppendLine("ms");
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatMilliseconds(double milliseconds) => milliseconds.ToString("F3", CultureInfo.InvariantCulture);

    public void Dispose()
    {
        _completionTimer?.Kill();
        _completionTimer = null;
        _capturing = false;
        _requester = null;
        _samples = null;
    }
}

internal readonly record struct PerformanceStatistics(int Count, double AverageMilliseconds, double P95Milliseconds, double MaxMilliseconds);

internal sealed class PerformanceSampleBuffer
{
    private sealed class MetricSamples
    {
        internal readonly long[] Values;
        internal int StoredCount;
        internal long TotalTicks;
        internal long MaxTicks;
        internal int Count;

        internal MetricSamples(int capacity) => Values = new long[capacity];

        internal void Reset()
        {
            StoredCount = 0;
            TotalTicks = 0;
            MaxTicks = 0;
            Count = 0;
        }
    }

    private readonly MetricSamples[] _metrics;

    internal PerformanceSampleBuffer(int samplesPerMetric)
    {
        _metrics = new MetricSamples[Enum.GetValues<PerformanceMetric>().Length];
        for (var i = 0; i < _metrics.Length; i++)
            _metrics[i] = new MetricSamples(samplesPerMetric);
    }

    internal void Reset()
    {
        foreach (var metric in _metrics)
            metric.Reset();
    }

    internal void Record(PerformanceMetric metric, long elapsedStopwatchTicks)
    {
        var samples = _metrics[(int)metric];
        var elapsed = Math.Max(0, elapsedStopwatchTicks);
        samples.Count++;
        samples.TotalTicks += elapsed;
        if (elapsed > samples.MaxTicks)
            samples.MaxTicks = elapsed;
        if (samples.StoredCount < samples.Values.Length)
            samples.Values[samples.StoredCount++] = elapsed;
    }

    internal PerformanceStatistics GetStatistics(PerformanceMetric metric)
    {
        var samples = _metrics[(int)metric];
        if (samples.Count == 0)
            return default;

        var sorted = new long[samples.StoredCount];
        Array.Copy(samples.Values, sorted, samples.StoredCount);
        Array.Sort(sorted);
        var p95Index = (int)Math.Ceiling(sorted.Length * 0.95d) - 1;
        var ticksToMilliseconds = 1000d / Stopwatch.Frequency;
        return new PerformanceStatistics(
            samples.Count,
            samples.TotalTicks * ticksToMilliseconds / samples.Count,
            sorted[Math.Max(0, p95Index)] * ticksToMilliseconds,
            samples.MaxTicks * ticksToMilliseconds);
    }
}
