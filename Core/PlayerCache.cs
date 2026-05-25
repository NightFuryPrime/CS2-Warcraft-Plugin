using System;
using System.Collections.Generic;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace WarcraftPlugin.Core
{
    internal static class PlayerCache
    {
        private static readonly List<CCSPlayerController> Empty = [];
        private static IReadOnlyList<CCSPlayerController> _players = Empty;
        private static double _snapshotTime = -1d;

        internal static IReadOnlyList<CCSPlayerController> GetPlayers()
        {
            var now = Server.TickedTime;
            if (Math.Abs(now - _snapshotTime) <= double.Epsilon)
                return _players;

            _players = Utilities.GetPlayers();
            _snapshotTime = now;
            return _players;
        }

        internal static void Invalidate()
        {
            _players = Empty;
            _snapshotTime = -1d;
        }
    }
}
