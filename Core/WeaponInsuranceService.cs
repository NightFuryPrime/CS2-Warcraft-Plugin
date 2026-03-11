using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using WarcraftPlugin.Items;
using WarcraftPlugin.Helpers;

namespace WarcraftPlugin.Core
{
    internal static class WeaponInsuranceService
    {
        private sealed class InsuranceState
        {
            internal bool TokenActive;
            internal bool PendingRespawn;
            internal readonly List<string> Weapons = [];
        }

        private static readonly Dictionary<ulong, InsuranceState> States = [];

        private static ulong GetKey(CCSPlayerController player)
        {
            if (player == null || !player.IsValid)
                return 0;

            if (player.SteamID != 0)
                return player.SteamID;

            var fallback = player.UserId ?? (int)player.Index;
            return unchecked((ulong)(uint)fallback);
        }

        private static InsuranceState GetOrCreateState(CCSPlayerController player)
        {
            var key = GetKey(player);
            if (key == 0)
                key = (ulong)player.Handle.ToInt64();

            if (!States.TryGetValue(key, out var state))
            {
                state = new InsuranceState();
                States[key] = state;
            }

            return state;
        }

        internal static void GrantInsurance(CCSPlayerController player)
        {
            if (player == null || !player.IsValid)
                return;

            var state = GetOrCreateState(player);
            state.TokenActive = true;
            state.PendingRespawn = false;
            state.Weapons.Clear();

            player.PrintToChat($" {ShopItem.Localizer["item.weapon_insurance.granted"]}");
        }

        internal static void HandlePlayerDeath(CCSPlayerController player)
        {
            if (player == null || !player.IsValid)
                return;

            var key = GetKey(player);
            if (!States.TryGetValue(key, out var state) || !state.TokenActive)
                return;

            var pawn = player.PlayerPawn.Value;
            var weaponServices = pawn?.WeaponServices;
            if (weaponServices?.MyWeapons == null)
                return;

            var snapshot = new List<string>();
            foreach (var weaponHandle in weaponServices.MyWeapons)
            {
                var weapon = weaponHandle?.Value;
                if (weapon == null || !weapon.IsValid) continue;

                var name = weapon.DesignerName;
                if (string.IsNullOrEmpty(name)) continue;

                if (name.StartsWith("weapon_knife", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("weapon_c4", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("weapon_taser", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (snapshot.Contains(name))
                    continue;

                snapshot.Add(name);
            }

            if (snapshot.Count == 0)
            {
                state.TokenActive = false;
                return;
            }

            state.Weapons.Clear();
            state.Weapons.AddRange(snapshot);
            state.TokenActive = false;
            state.PendingRespawn = true;
        }

        internal static void HandlePlayerSpawn(CCSPlayerController player)
        {
            if (player == null || !player.IsValid)
                return;

            var key = GetKey(player);
            if (!States.TryGetValue(key, out var state) || !state.PendingRespawn || state.Weapons.Count == 0)
                return;

            state.PendingRespawn = false;
            var loadout = state.Weapons.ToList();
            state.Weapons.Clear();

            Server.NextFrame(() =>
            {
                if (player == null || !player.IsValid)
                    return;

                foreach (var weaponName in loadout)
                {
                    try
                    {
                        player.GiveWeapon(weaponName);
                    }
                    catch
                    {
                        // ignore failures for weapons that can no longer be given
                    }
                }

                player.PrintToChat($" {ShopItem.Localizer["item.weapon_insurance.applied"]}");
            });
        }

        internal static void Clear(CCSPlayerController player)
        {
            var key = GetKey(player);
            States.Remove(key);
        }

        internal static void Reset()
        {
            States.Clear();
        }
    }
}
