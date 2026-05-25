using System;
using System.Collections.Generic;
using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using WarcraftPlugin.Diagnostics;
using static CounterStrikeSharp.API.Core.Listeners;

namespace WarcraftPlugin.Core.Effects
{
    public class EffectManager : IDisposable
    {
        private readonly List<WarcraftEffect> _effects = [];
        public static readonly float _tickRate = Server.TickInterval; // Lowest possible interval.
        private bool _initialized;
        private bool _disposed;

        internal void Initialize()
        {
            if (_initialized)
                return;

            WarcraftPlugin.Instance.RegisterListener<OnTick>(EffectTick);
            _initialized = true;
        }

        internal void AddEffect(WarcraftEffect effect)
        {
            if (effect == null || _disposed)
                return;

            _effects.Add(effect);
            if (!TryRun(effect, static e => e.OnStart(), nameof(WarcraftEffect.OnStart)))
            {
                _effects.Remove(effect);
            }
        }

        private void EffectTick()
        {
            if (_effects.Count == 0)
                return;

            for (int i = _effects.Count - 1; i >= 0;)
            {
                if (i >= _effects.Count)
                {
                    i = _effects.Count - 1;
                    continue;
                }

                var effect = _effects[i];
                if (effect?.Owner?.IsValid != true)
                {
                    RemoveAt(i, finish: effect?.FinishOnDestroy == true);
                    i--;
                    continue;
                }

                effect.RemainingDuration -= _tickRate;

                if (effect.RemainingDuration <= 0)
                {
                    RemoveAt(i, finish: true);
                    i--;
                    continue;
                }

                var elapsedTime = effect.Duration - effect.RemainingDuration;
                if (effect.TickEnabled && elapsedTime - effect.LastTick >= effect.OnTickInterval)
                {
                    if (!TryRun(effect, static e => e.OnTick(), nameof(WarcraftEffect.OnTick)))
                    {
                        RemoveAt(i, finish: effect.FinishOnDestroy);
                        i--;
                        continue;
                    }

                    effect.LastTick = elapsedTime;
                }

                i--;
            }
        }

        public List<WarcraftEffect> GetEffects()
        {
            return _effects;
        }

        public List<T> GetEffectsByType<T>() where T : WarcraftEffect
        {
            return GetEffects().FindAll(x => x is T).Cast<T>().ToList();
        }

        internal void DestroyEffects(CCSPlayerController player, EffectDestroyFlags flag)
        {
            if (player == null)
                return;

            DestroyEffects(player.Handle, flag);
        }

        internal void DestroyEffects(IntPtr ownerHandle, EffectDestroyFlags flag)
        {
            if (ownerHandle == IntPtr.Zero)
                return;

            for (int i = _effects.Count - 1; i >= 0; i--)
            {
                var effect = _effects[i];
                if (effect.Owner?.Handle == ownerHandle && effect.ShouldDestroy(flag))
                {
                    RemoveAt(i, effect.FinishOnDestroy);
                }
            }
        }

        internal void DestroyEffect(WarcraftEffect effect)
        {
            var index = _effects.IndexOf(effect);
            if (index >= 0)
            {
                RemoveAt(index, effect.FinishOnDestroy);
            }
        }

        internal void DestroyAllEffects(bool finishEffects = true)
        {
            if (finishEffects)
            {
                for (int i = _effects.Count - 1; i >= 0; i--)
                {
                    RemoveAt(i, finish: true);
                }

                return;
            }

            _effects.Clear();
        }

        private void RemoveAt(int index, bool finish)
        {
            if (index < 0 || index >= _effects.Count)
                return;

            var effect = _effects[index];
            if (finish)
            {
                TryRun(effect, static e => e.OnFinish(), nameof(WarcraftEffect.OnFinish));
            }

            _effects.RemoveAt(index);
        }

        private static bool TryRun(WarcraftEffect effect, Action<WarcraftEffect> action, string operation)
        {
            try
            {
                action(effect);
                return true;
            }
            catch (Exception ex)
            {
                PersistentLogger.Error(
                    nameof(EffectManager),
                    $"{operation} failed for {effect?.GetType().Name ?? "unknown effect"}.",
                    ex);
                Console.WriteLine($"[WarcraftPlugin] {operation} failed for {effect?.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            if (_initialized)
            {
                WarcraftPlugin.Instance?.RemoveListener<OnTick>(EffectTick);
                _initialized = false;
            }

            DestroyAllEffects(finishEffects: true);
            _disposed = true;
        }
    }
}
