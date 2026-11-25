using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Utils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using WarcraftPlugin.Helpers;
using WarcraftPlugin.Diagnostics;
using static CounterStrikeSharp.API.Core.Listeners;

namespace WarcraftPlugin.Core;

internal static class WarcraftInvisibilityManager
{
    private sealed class InvisibleState
    {
        public Color OriginalRender { get; init; }
        public HashSet<string> Tokens { get; } = [];
    }

    private static readonly Dictionary<IntPtr, InvisibleState> InvisiblePlayers = [];
    private static readonly List<CCSPlayerController> InvisiblePlayerSnapshot = [];
    private static bool _transmitRegistered;

    internal static int ActiveCount => InvisiblePlayers.Count;

    internal static void Add(CCSPlayerController player, string token, float percentInvisibility = 1f)
    {
        if (player == null || !player.IsValid || string.IsNullOrWhiteSpace(token))
            return;

        var pawn = player.PlayerPawn?.Value;
        if (pawn == null || !pawn.IsValid)
            return;

        if (!InvisiblePlayers.TryGetValue(player.Handle, out var state))
        {
            state = new InvisibleState { OriginalRender = pawn.Render };
            InvisiblePlayers[player.Handle] = state;
        }

        state.Tokens.Add(token);
        ApplyRender(player, percentInvisibility);
        EnsureTransmitRegistered();
    }

    internal static void Remove(CCSPlayerController player, string token)
    {
        if (player == null || string.IsNullOrWhiteSpace(token))
            return;

        if (!InvisiblePlayers.TryGetValue(player.Handle, out var state))
            return;

        state.Tokens.Remove(token);
        if (state.Tokens.Count > 0)
            return;

        InvisiblePlayers.Remove(player.Handle);
        RestoreRender(player, state.OriginalRender);
        UnregisterTransmitIfUnused();
    }

    internal static bool IsInvisible(CCSPlayerController player)
    {
        return player != null && InvisiblePlayers.ContainsKey(player.Handle);
    }

    internal static void Reset()
    {
        foreach (var player in PlayerCache.GetPlayers())
        {
            if (player == null || !InvisiblePlayers.TryGetValue(player.Handle, out var state))
                continue;

            RestoreRender(player, state.OriginalRender);
        }

        InvisiblePlayers.Clear();
        UnregisterTransmitIfUnused(force: true);
    }

    internal static void ResetPlayer(IntPtr playerHandle)
    {
        if (playerHandle == IntPtr.Zero)
            return;

        InvisiblePlayers.Remove(playerHandle);
        UnregisterTransmitIfUnused();
    }

    private static void ApplyRender(CCSPlayerController player, float percentInvisibility)
    {
        var pawn = player.PlayerPawn?.Value;
        if (pawn == null || !pawn.IsValid)
            return;

        var alpha = Math.Clamp(255 - (int)(255 * percentInvisibility), 0, 255);
        pawn.SetColor(Color.FromArgb(alpha, 255, 255, 255));
    }

    private static void RestoreRender(CCSPlayerController player, Color color)
    {
        if (player?.TryGetAlivePawn(out var pawn) != true)
            return;

        pawn.SetColor(color);
    }

    private static void EnsureTransmitRegistered()
    {
        if (_transmitRegistered || WarcraftPlugin.Instance == null)
            return;

        WarcraftPlugin.Instance.RegisterListener<CheckTransmit>(CheckTransmit);
        _transmitRegistered = true;
    }

    private static void UnregisterTransmitIfUnused(bool force = false)
    {
        if (!_transmitRegistered || WarcraftPlugin.Instance == null)
            return;

        if (!force && InvisiblePlayers.Count > 0)
            return;

        WarcraftPlugin.Instance.RemoveListener<CheckTransmit>(CheckTransmit);
        _transmitRegistered = false;
    }

    private static void CheckTransmit([CastFrom(typeof(nint))] CCheckTransmitInfoList infoList)
    {
        using var measurement = WarcraftPlugin.Instance?.PerformanceDiagnostics?.Measure(PerformanceMetric.InvisibilityTransmit) ?? default;
        if (InvisiblePlayers.Count == 0)
            return;

        BuildInvisiblePlayerSnapshot();
        try
        {
            foreach (var (info, viewer) in infoList)
            {
                if (viewer == null || !viewer.IsValid || viewer.Team == CsTeam.Spectator)
                    continue;

                foreach (var hidden in InvisiblePlayerSnapshot)
                {
                    if (hidden == null || !hidden.IsValid || hidden.Index == viewer.Index)
                        continue;

                    if (viewer.AllyOf(hidden) || IsObserverTarget(viewer, hidden))
                        continue;

                    RemovePlayerEntitiesFromTransmit(info.TransmitEntities, hidden);
                }
            }
        }
        finally
        {
            InvisiblePlayerSnapshot.Clear();
        }
    }

    private static void BuildInvisiblePlayerSnapshot()
    {
        InvisiblePlayerSnapshot.Clear();
        foreach (var player in PlayerCache.GetPlayers())
        {
            if (player != null && player.PawnIsAlive && InvisiblePlayers.ContainsKey(player.Handle))
                InvisiblePlayerSnapshot.Add(player);
        }
    }

    private static bool IsObserverTarget(CCSPlayerController viewer, CCSPlayerController hidden)
    {
        var targetHandle = viewer.Pawn?.Value?.ObserverServices?.ObserverTarget.Value?.Handle ?? IntPtr.Zero;
        return targetHandle != IntPtr.Zero && hidden.Pawn?.Value?.Handle == targetHandle;
    }

    private static void RemovePlayerEntitiesFromTransmit(CFixedBitVecBase transmitEntities, CCSPlayerController player)
    {
        var pawn = player.PlayerPawn?.Value;
        if (pawn == null || !pawn.IsValid)
            return;

        RemoveEntity(transmitEntities, pawn);

        var weaponServices = pawn.WeaponServices;
        if (weaponServices == null)
            return;

        var activeWeapon = weaponServices.ActiveWeapon?.Value;
        if (activeWeapon != null && activeWeapon.IsValid)
            RemoveEntity(transmitEntities, activeWeapon);

        foreach (var weaponHandle in weaponServices.MyWeapons)
        {
            var weapon = weaponHandle?.Value;
            if (weapon != null && weapon.IsValid &&
                (activeWeapon == null || weapon.EntityHandle.Raw != activeWeapon.EntityHandle.Raw))
                RemoveEntity(transmitEntities, weapon);
        }
    }

    private static void RemoveEntity(CFixedBitVecBase transmitEntities, CEntityInstance entity)
    {
        if (entity == null || !entity.IsValid)
            return;

        transmitEntities.Remove(entity);
    }
}
