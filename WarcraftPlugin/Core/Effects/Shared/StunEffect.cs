using CounterStrikeSharp.API.Core;
using WarcraftPlugin.Helpers;

namespace WarcraftPlugin.Core.Effects.Shared
{
    public static class StunEffectExtensions
    {
        /// <summary>
        /// Stuns the player by setting their movement speed to 0 for a duration.
        /// </summary>
        /// <param name="player">The player controller to stun.</param>
        /// <param name="duration">How long the stun should last in seconds.</param>
        public static void Stun(this CCSPlayerController player, float duration, CCSPlayerController attacker = null, string abilityName = null)
        {
            if (!player.IsAlive()) return;

            new StunEffect(player, attacker, abilityName, duration).Start();
        }
    }

    public class StunEffect : WarcraftEffect
    {
        private CParticleSystem _particle;
        private readonly CCSPlayerController _attacker;
        private readonly string _abilityName;

        public StunEffect(CCSPlayerController owner, CCSPlayerController attacker, string abilityName, float duration)
            : base(owner, duration, onTickInterval: 0f)
        {
            _attacker = attacker;
            _abilityName = abilityName;
        }

        public override void OnStart()
        {
            if (!TryGetAliveOwnerPawn(out var pawn)) return;
            SetMaxSpeedMultiplier(Owner, 0f, "stun");
            SetVelocityMultiplier(Owner, 0f, "stun");
            RefreshPlayerState(Owner);

            _particle = Warcraft.SpawnParticle(Owner.EyePosition(-60), "particles/ui/ammohealthcenter/ui_hud_kill_streaks_circleglow.vpcf", Duration);
            _particle?.SetParent(pawn);

            if (!string.IsNullOrEmpty(_abilityName))
            {
                Owner.PrintToChat($" {Localizer["effect.stun.stunnedby", _abilityName]}");
                _attacker?.PrintToChat($" {Localizer["effect.stun.stunowner", _abilityName, Owner.GetRealPlayerName()]}");
            }
        }

        public override void OnTick() { }

        public override void OnFinish()
        {
            RemoveStateContributions(Owner, "stun");
            RefreshPlayerState(Owner);
            _particle.RemoveIfValid();
        }
    }
}
