namespace CS2MenuManager.API.Class;

internal sealed class MenuSessionTracker
{
    private readonly Dictionary<ulong, long> _generations = [];

    internal long Begin(ulong steamId)
    {
        var generation = _generations.TryGetValue(steamId, out var current) ? current + 1 : 1;
        _generations[steamId] = generation;
        return generation;
    }

    internal bool IsCurrent(ulong steamId, long generation)
    {
        return _generations.TryGetValue(steamId, out var current) && current == generation;
    }

    internal void Cancel(ulong steamId)
    {
        _generations[steamId] = _generations.TryGetValue(steamId, out var current) ? current + 1 : 1;
    }

    internal void CancelAll()
    {
        var steamIds = _generations.Keys.ToArray();
        foreach (var steamId in steamIds)
            _generations[steamId]++;
    }
}
