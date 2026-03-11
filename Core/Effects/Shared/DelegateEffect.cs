using CounterStrikeSharp.API.Core;
using System;

namespace WarcraftPlugin.Core.Effects.Shared
{
    internal static class EffectBuilder
    {
        internal static void Run(
            CCSPlayerController owner,
            float duration,
            Action onStart,
            Action onFinish = null,
            Action onTick = null,
            bool destroyOnDeath = true,
            bool destroyOnRoundEnd = true,
            bool destroyOnChangingRace = true,
            bool destroyOnDisconnect = true,
            bool destroyOnSpawn = true,
            bool finishOnDestroy = true,
            float onTickInterval = 0.25f)
        {
            new DelegateEffect(owner, duration, onStart, onTick, onFinish,
                destroyOnDeath, destroyOnRoundEnd, destroyOnChangingRace, destroyOnDisconnect, destroyOnSpawn, finishOnDestroy, onTickInterval)
                .Start();
        }
    }

    internal sealed class DelegateEffect : WarcraftEffect
    {
        private readonly Action _onStart;
        private readonly Action _onTick;
        private readonly Action _onFinish;

        internal DelegateEffect(
            CCSPlayerController owner,
            float duration,
            Action onStart,
            Action onTick,
            Action onFinish,
            bool destroyOnDeath,
            bool destroyOnRoundEnd,
            bool destroyOnChangingRace,
            bool destroyOnDisconnect,
            bool destroyOnSpawn,
            bool finishOnDestroy,
            float onTickInterval)
            : base(owner, duration, destroyOnDeath, destroyOnRoundEnd, destroyOnChangingRace, destroyOnDisconnect, destroyOnSpawn, finishOnDestroy, onTickInterval)
        {
            _onStart = onStart;
            _onTick = onTick;
            _onFinish = onFinish;
        }

        public override void OnStart() => _onStart?.Invoke();
        public override void OnTick() => _onTick?.Invoke();
        public override void OnFinish() => _onFinish?.Invoke();
    }
}
