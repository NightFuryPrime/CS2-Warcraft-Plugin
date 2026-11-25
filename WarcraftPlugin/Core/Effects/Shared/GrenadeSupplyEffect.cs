using System.Linq;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using WarcraftPlugin.Helpers;

namespace WarcraftPlugin.Core.Effects.Shared
{
    internal sealed class GrenadeSupplyEffect(
        CCSPlayerController owner,
        string grenadeName,
        float grenadeInterval,
        string displayName)
        : WarcraftEffect(owner, onTickInterval: grenadeInterval)
    {
        public override void OnStart() { }

        public override void OnTick()
        {
            if (!Owner.TryGetAlivePawn(out var pawn))
                return;

            var services = pawn.WeaponServices;
            if (services?.MyWeapons == null)
                return;

            var hasGrenade = services.MyWeapons.Any(w => w.Value?.DesignerName == grenadeName);
            if (hasGrenade)
                return;

            Owner.GiveWeapon<CBasePlayerWeapon>(grenadeName);
            Owner.PrintToChat($" {ChatColors.Green}+{displayName}{ChatColors.Default}");
        }

        public override void OnFinish() { }
    }
}
