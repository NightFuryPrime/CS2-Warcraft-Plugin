using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Localization;
using System;

using WarcraftPlugin.Core;
using WarcraftPlugin.Helpers;

namespace WarcraftPlugin.Core.Effects
{
    /// <summary>
    /// Represents an abstract base class for Warcraft effects.
    /// </summary>
    /// <param name="owner">The player controller that owns this effect.</param>
    /// <param name="duration">The duration of the effect in seconds. If not supplied, effect will continue until destroyed.</param>
    /// <param name="destroyOnDeath">Indicates whether the effect should be destroyed on owner death. Defaults to true.</param>
    /// <param name="destroyOnRoundEnd">Indicates whether the effect should be destroyed at the end of the round. Defaults to true.</param>
    /// <param name="destroyOnChangingRace">Indicates whether the effect should be destroyed when owner changes race. Defaults to true.</param>
    /// <param name="destroyOnDisconnect">Indicates whether the effect should be destroyed on owner disconnect. Defaults to true.</param>
    /// <param name="destroyOnSpawn">Indicates whether the effect should be destroyed on owner spawn. Defaults to true.</param>
    /// <param name="finishOnDestroy">Indicates whether the effect should run the finish method when destroyed. Defaults to true.</param>
    /// <param name="onTickInterval">The interval in seconds at which the effect's OnTick method is called. Defaults to 0.25 seconds.</param>
    /// <remarks>
    /// All destroy events happen in the pre hook, before invoke.
    /// </remarks>
    public abstract class WarcraftEffect(CCSPlayerController owner, float duration = 100000,
            bool destroyOnDeath = true, bool destroyOnRoundEnd = true,
            bool destroyOnChangingRace = true, bool destroyOnDisconnect = true,
            bool destroyOnSpawn = true, bool finishOnDestroy = true, float onTickInterval = 0.25f)
    {
        private string _stateContributionKeyPrefix;

        /// <summary>
        /// Gets the player controller that owns this effect.
        /// </summary>
        public CCSPlayerController Owner { get; } = owner;

        /// <summary>
        /// Gets the duration of the effect in seconds.
        /// </summary>
        public float Duration { get; set; } = duration;

        /// <summary>
        /// Gets or sets the remaining duration of the effect in seconds.
        /// </summary>
        public float RemainingDuration { get; set; } = duration;

        /// <summary>
        /// Gets or sets the time of the last tick in seconds.
        /// </summary>
        public float LastTick { get; set; } = 0;

        /// <summary>
        /// Gets a value indicating whether the effect should finish when destroyed.
        /// </summary>
        public bool FinishOnDestroy { get; set; } = finishOnDestroy;

        /// <summary>
        /// Gets the interval in seconds at which the effect's OnTick method is called.
        /// </summary>
        public bool TickEnabled { get; set; } = onTickInterval > 0f;

        public float OnTickInterval { get; set; } = onTickInterval > 0f
            ? Math.Max(onTickInterval, EffectManager._tickRate)
            : float.MaxValue;

        /// <summary>
        /// Gets the flags indicating the conditions under which the effect should be destroyed.
        /// </summary>
        public EffectDestroyFlags DestroyFlags = (destroyOnDeath ? EffectDestroyFlags.OnDeath : 0) |
                       (destroyOnRoundEnd ? EffectDestroyFlags.OnRoundEnd : 0) |
                       (destroyOnChangingRace ? EffectDestroyFlags.OnChangingRace : 0) |
                       (destroyOnDisconnect ? EffectDestroyFlags.OnDisconnect : 0) |
                       (destroyOnSpawn ? EffectDestroyFlags.OnSpawn : 0);

        public readonly IStringLocalizer Localizer = WarcraftPlugin.Instance.Localizer;

        /// <summary>
        /// Called when the effect starts.
        /// </summary>
        public abstract void OnStart();

        /// <summary>
        /// Called at regular intervals defined by OnTickInterval.
        /// </summary>
        public abstract void OnTick();

        /// <summary>
        /// Called when the effect finishes.
        /// </summary>
        public abstract void OnFinish();

        /// <summary>
        /// Determines whether the effect should be destroyed based on the specified condition.
        /// </summary>
        /// <param name="condition">The condition to check against the destroy flags.</param>
        /// <returns>True if the effect should be destroyed; otherwise, false.</returns>
        public bool ShouldDestroy(EffectDestroyFlags condition) => (DestroyFlags & condition) != 0;

        public void Destroy() => WarcraftPlugin.Instance.EffectManager.DestroyEffect(this);
        public void Start() => WarcraftPlugin.Instance.EffectManager.AddEffect(this);

        protected bool TryGetAliveOwnerPawn(out CCSPlayerPawn pawn) => Owner.TryGetAlivePawn(out pawn);

        protected bool TryGetOwnerActiveWeapon(out CBasePlayerWeapon weapon) => Owner.TryGetActiveWeapon(out weapon);

        protected string StateKey(string suffix = null)
        {
            _stateContributionKeyPrefix ??= $"{GetType().FullName}:{Guid.NewGuid():N}";
            return string.IsNullOrWhiteSpace(suffix)
                ? _stateContributionKeyPrefix
                : $"{_stateContributionKeyPrefix}:{suffix}";
        }

        protected void RemoveStateContributions(CCSPlayerController player, string suffix = null)
        {
            DerivedPlayerStateManager.RemoveContribution(player, StateKey(suffix));
        }

        protected void SetVelocityMultiplier(CCSPlayerController player, float multiplier, string suffix = null)
        {
            DerivedPlayerStateManager.SetVelocityMultiplier(player, StateKey(suffix), multiplier);
        }

        protected void SetVelocityAdditive(CCSPlayerController player, float additive, string suffix = null)
        {
            DerivedPlayerStateManager.SetVelocityAdditive(player, StateKey(suffix), additive);
        }

        protected void SetGravityMultiplier(CCSPlayerController player, float multiplier, string suffix = null)
        {
            DerivedPlayerStateManager.SetGravityMultiplier(player, StateKey(suffix), multiplier);
        }

        protected void SetGravityAdditive(CCSPlayerController player, float additive, string suffix = null)
        {
            DerivedPlayerStateManager.SetGravityAdditive(player, StateKey(suffix), additive);
        }

        protected void SetMaxSpeedMultiplier(CCSPlayerController player, float multiplier, string suffix = null)
        {
            DerivedPlayerStateManager.SetMaxSpeedMultiplier(player, StateKey(suffix), multiplier);
        }

        protected void SetMaxHealthBonus(CCSPlayerController player, int bonus, string suffix = null)
        {
            DerivedPlayerStateManager.SetMaxHealthBonus(player, StateKey(suffix), bonus);
        }

        protected void RefreshPlayerState(CCSPlayerController player, bool resetBaseline = false)
        {
            DerivedPlayerStateManager.RefreshDerivedPlayerState(player, resetBaseline);
        }
    }

    /// <summary>
    /// Specifies the conditions under which a Warcraft effect should be destroyed.
    /// </summary>
    [Flags]
    public enum EffectDestroyFlags
    {
        /// <summary>
        /// No conditions.
        /// </summary>
        None = 0,

        /// <summary>
        /// Destroy on death.
        /// </summary>
        OnDeath = 1 << 0,         // 1

        /// <summary>
        /// Destroy at the end of the round.
        /// </summary>
        OnRoundEnd = 1 << 1,      // 2

        /// <summary>
        /// Destroy when changing race.
        /// </summary>
        OnChangingRace = 1 << 2,  // 4

        /// <summary>
        /// Destroy on disconnect.
        /// </summary>
        OnDisconnect = 1 << 3,    // 8

        /// <summary>
        /// Destroy on spawn.
        /// </summary>
        OnSpawn = 1 << 4,         // 16
    }
}
