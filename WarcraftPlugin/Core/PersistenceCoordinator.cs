using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace WarcraftPlugin.Core
{
    internal readonly record struct DirtyPlayerProgress(PlayerProgressSnapshot Snapshot, long Version);

    internal sealed class PersistenceCoordinator
    {
        private readonly ConcurrentDictionary<long, DirtyPlayerProgress> _dirtyPlayers = new();
        private long _nextVersion;

        internal int DirtyCount => _dirtyPlayers.Count;

        internal DirtyPlayerProgress MarkDirty(PlayerProgressSnapshot snapshot)
        {
            var dirtyPlayer = new DirtyPlayerProgress(snapshot, Interlocked.Increment(ref _nextVersion));
            _dirtyPlayers.AddOrUpdate(snapshot.SteamId, dirtyPlayer, (_, _) => dirtyPlayer);
            return dirtyPlayer;
        }

        internal bool TryGetDirty(long steamId, out DirtyPlayerProgress dirtyPlayer)
        {
            return _dirtyPlayers.TryGetValue(steamId, out dirtyPlayer);
        }

        internal IReadOnlyList<DirtyPlayerProgress> SnapshotDirtyPlayers()
        {
            return _dirtyPlayers.Values
                .OrderBy(entry => entry.Snapshot.SteamId)
                .ToArray();
        }

        internal void CompleteFlush(IEnumerable<DirtyPlayerProgress> flushedEntries)
        {
            foreach (var flushedEntry in flushedEntries)
            {
                if (_dirtyPlayers.TryGetValue(flushedEntry.Snapshot.SteamId, out var currentEntry) &&
                    currentEntry.Version == flushedEntry.Version)
                {
                    _dirtyPlayers.TryRemove(flushedEntry.Snapshot.SteamId, out _);
                }
            }
        }

        internal void Clear(long steamId)
        {
            _dirtyPlayers.TryRemove(steamId, out _);
        }

        internal void ClearAll()
        {
            _dirtyPlayers.Clear();
        }
    }
}
