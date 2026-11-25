namespace WarcraftPlugin.Core;

internal sealed class QueuePressureMonitor
{
    private readonly int _queueThreshold;
    private readonly int _requiredConsecutiveSamples;
    private readonly int _repeatIntervalSamples;
    private int _consecutiveHighSamples;

    internal QueuePressureMonitor(int queueThreshold = 20, int requiredConsecutiveSamples = 2, int repeatIntervalSamples = 4)
    {
        _queueThreshold = queueThreshold;
        _requiredConsecutiveSamples = requiredConsecutiveSamples;
        _repeatIntervalSamples = repeatIntervalSamples;
    }

    internal bool Observe(int queueDepth)
    {
        if (queueDepth < _queueThreshold)
        {
            _consecutiveHighSamples = 0;
            return false;
        }

        _consecutiveHighSamples++;
        if (_consecutiveHighSamples < _requiredConsecutiveSamples)
            return false;

        return (_consecutiveHighSamples - _requiredConsecutiveSamples) % _repeatIntervalSamples == 0;
    }
}
