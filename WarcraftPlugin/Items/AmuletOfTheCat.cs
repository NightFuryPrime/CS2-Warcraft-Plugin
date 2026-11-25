using CounterStrikeSharp.API.Core;
using WarcraftPlugin.Core.Effects;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;
using WarcraftPlugin.Core;

namespace WarcraftPlugin.Items;

internal class AmuletOfTheCat : ShopItem
{
    protected override string Name => "Amulet of the Cat";
    protected override FormattableString Description => $"Silent Footsteps";
    internal override int Price { get; set; } = 4000;
    internal override Color Color { get; set; } = Color.FromArgb(255, 192, 192, 192); // Silver for stealth/unique

    internal override void Apply(CCSPlayerController player)
    {
        new SilentFootstepsEffect(player).Start();
    }

    internal static void ResetSilentFootstepState()
    {
        SilentFootstepsEffect.Reset();
    }

    private class SilentFootstepsEffect(CCSPlayerController owner) : WarcraftEffect(owner, onTickInterval: 0f)
    {
        private static readonly HashSet<uint> FootstepSoundEvents =
        [
            3109879199, 70939233, 1342713723, 2722081556, 1909915699, 3193435079, 2300993891, 3847761506,
            4084367249, 2026488395, 2745524735, 2684452812, 2265091453, 1269567645, 520432428, 3266483468,
            1346129716, 2061955732, 2240518199, 2829617974, 1194677450, 1803111098, 3749333696, 29217150,
            1692050905, 2207486967, 2633527058, 3342414459, 988265811, 540697918, 1763490157, 3755338324,
            3161194970, 3753692454, 3166948458, 3997353267, 809738584, 3368720745, 3295206520, 3184465677,
            123085364, 3123711576, 737696412, 1403457606, 1770765328, 892882552, 3023174225, 4163677892,
            3952104171, 4082928848, 1019414932, 1485322532, 1161855519, 1557420499, 1163426340, 2708661994,
            2479376962, 1404198078, 1194093029, 1253503839, 2189706910, 1218015996, 96240187, 1116700262,
            84876002, 1598540856, 2231399653
        ];

        private static readonly HashSet<uint> SilentSoundEvents =
        [
            2551626319, 765706800, 2860219006, 2162652424, 117596568, 740474905, 1661204257, 3009312615,
            1506215040, 115843229, 3299941720, 1016523349, 2684452812, 2067683805, 4160462271, 1543118744,
            585390608, 3802757032, 2302139631, 2546391140, 144629619, 4152012084, 4113422219, 1627020521,
            2899365092, 819435812, 3218103073, 961838155, 1535891875, 1826799645, 3460445620, 1818046345,
            3666896632, 3099536373, 1440734007, 1409986305, 1939055066, 782454593, 4074593561, 1540837791,
            3257325156
        ];

        // Tracks controllers, not pawn handles, so the item survives death/respawn.
        private static readonly HashSet<ulong> _silentPlayers = [];
        private static bool _hooked;
        private ulong _ownerKey;

        public override void OnStart()
        {
            var pawn = Owner?.PlayerPawn?.Value;
            if (pawn == null) { Destroy(); return; }

            if (!_hooked)
            {
                WarcraftPlugin.Instance.HookUserMessage(208, PreFootstepMessage, HookMode.Pre);
                _hooked = true;
            }

            _ownerKey = GetPlayerKey(Owner);
            _silentPlayers.Add(_ownerKey);
            Owner.PrintToChat($" {Localizer["item.amulet_of_the_cat.activated"]}");
        }

        private static HookResult PreFootstepMessage(UserMessage um)
        {
            var soundEvent = um.ReadUInt("soundevent_hash");
            if (!FootstepSoundEvents.Contains(soundEvent) && !SilentSoundEvents.Contains(soundEvent))
                return HookResult.Continue;

            var entityIndex = um.ReadInt("source_entity_index");
            var player = PlayerCache.GetPlayers().FirstOrDefault(p =>
                p.PlayerPawn?.Value?.Index == entityIndex);

            if (player == null)
                return HookResult.Continue;

            if (!_silentPlayers.Contains(GetPlayerKey(player)))
                return HookResult.Continue;

            um.Recipients.Clear();
            return HookResult.Stop;
        }

        public override void OnTick() { }

        public override void OnFinish()
        {
            if (_ownerKey != 0)
            {
                _silentPlayers.Remove(_ownerKey);
                _ownerKey = 0;
            }

            if (_silentPlayers.Count == 0 && _hooked)
            {
                WarcraftPlugin.Instance?.UnhookUserMessage(208, PreFootstepMessage, HookMode.Pre);
                _hooked = false;
            }
        }

        internal static void Reset()
        {
            _silentPlayers.Clear();
            if (!_hooked)
                return;

            WarcraftPlugin.Instance?.UnhookUserMessage(208, PreFootstepMessage, HookMode.Pre);
            _hooked = false;
        }

        private static ulong GetPlayerKey(CCSPlayerController player)
        {
            if (player?.UserId is int userId && userId > 0)
                return (uint)userId;

            if (player?.SteamID > 0)
                return player.SteamID;

            return unchecked((ulong)(player?.Handle.ToInt64() ?? 0));
        }
    }
}
