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
        // Tracks all players currently under the silent footstep effect
        private static readonly HashSet<IntPtr> _silentPlayers = [];
        private static bool _hooked;
        private IntPtr _pawnHandle;

        public override void OnStart()
        {
            var pawn = Owner?.PlayerPawn?.Value;
            if (pawn == null) { Destroy(); return; }

            if (!_hooked)
            {
                WarcraftPlugin.Instance.HookUserMessage(208, PreFootstepMessage, HookMode.Pre);
                _hooked = true;
            }

            _pawnHandle = pawn.Handle;
            _silentPlayers.Add(_pawnHandle);
            Owner.PrintToChat($" {Localizer["item.amulet_of_the_cat.activated"]}");
        }

        private static HookResult PreFootstepMessage(UserMessage um)
        {
            var entityIndex = um.ReadInt("source_entity_index");
            var player = PlayerCache.GetPlayers().FirstOrDefault(p =>
                p.PlayerPawn?.Value?.Index == entityIndex);

            if (player == null)
                return HookResult.Continue;

            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !_silentPlayers.Contains(pawn.Handle))
                return HookResult.Continue;

            um.Recipients.Clear();
            return HookResult.Stop;
        }

        public override void OnTick() { }

        public override void OnFinish()
        {
            if (_pawnHandle != IntPtr.Zero)
            {
                _silentPlayers.Remove(_pawnHandle);
                _pawnHandle = IntPtr.Zero;
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
    }
}
