using System;
using System.Collections.Generic;
using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using WarcraftPlugin.Models;
using WarcraftPlugin.Helpers;
using static CounterStrikeSharp.API.Core.Listeners;

namespace WarcraftPlugin.Core
{
    internal sealed class BotUltimateController
    {
        private readonly Dictionary<string, Func<CCSPlayerController, WarcraftPlayer, WarcraftClass, bool>> _profiles;
        private readonly Dictionary<nint, float> _nextBotEvaluation = new();
        private OnTick _tickListener;
        private float _nextThinkTime;

        internal BotUltimateController()
        {
            _profiles = BuildProfiles();
        }

        internal void Initialize()
        {
            _tickListener ??= new OnTick(DriveBotUltimates);
            WarcraftPlugin.Instance.RegisterListener(_tickListener);
        }

        internal void Dispose()
        {
            if (_tickListener != null)
            {
                WarcraftPlugin.Instance.RemoveListener(_tickListener);
                _tickListener = null;
            }
            _nextBotEvaluation.Clear();
        }

        private void DriveBotUltimates()
        {
            if (Server.CurrentTime < _nextThinkTime) return;
            _nextThinkTime = Server.CurrentTime + 0.25f;

            foreach (var bot in Utilities.GetPlayers().Where(p => p.IsBot && !p.ControllingBot && p.PawnIsAlive))
            {
                if (!ShouldEvaluate(bot)) continue;

                var wcPlayer = bot.GetWarcraftPlayer();
                if (wcPlayer == null) continue;
                if (wcPlayer.GetAbilityLevel(WarcraftPlayer.UltimateAbilityIndex) <= 0) continue;

                var warcraftClass = wcPlayer.GetClass();
                if (warcraftClass == null || !warcraftClass.IsAbilityReady(WarcraftPlayer.UltimateAbilityIndex)) continue;

                if (_profiles.TryGetValue(warcraftClass.InternalName, out var rule))
                {
                    if (rule(bot, wcPlayer, warcraftClass)) continue;
                }
                else
                {
                    TryBurst(bot, warcraftClass, 550f, 0.35f);
                }
            }
        }

        private Dictionary<string, Func<CCSPlayerController, WarcraftPlayer, WarcraftClass, bool>> BuildProfiles()
        {
            return new(StringComparer.OrdinalIgnoreCase)
            {
                ["barbarian"] = (bot, _, cls) => TryBurst(bot, cls, 650f, 0.5f),
                ["dwarf_engineer"] = (bot, _, cls) => TryBurst(bot, cls, 600f, 0.6f),
                ["hammerstorm"] = (bot, _, cls) => TryBurst(bot, cls, 600f, 0.45f),
                ["mage"] = (bot, _, cls) => TryMageDash(bot, cls),
                ["necromancer"] = (bot, _, cls) => TrySummon(bot, cls, 1100f),
                ["paladin"] = (bot, _, cls) => TryRevive(bot, cls),
                ["death_weaver"] = (bot, _, cls) => TryRevive(bot, cls),
                ["ranger"] = (bot, _, cls) => TryArrowstorm(bot, cls),
                ["sacred_warrior"] = (bot, _, cls) => TryLifeBreak(bot, cls),
                ["shadowblade"] = (bot, _, cls) => TryBurst(bot, cls, 500f, 0.55f),
                ["silent_assassin"] = (bot, _, cls) => TryBurst(bot, cls, 420f, 0.65f),
                ["shapeshifter"] = (bot, _, cls) => TryBurst(bot, cls, 450f, 0.45f),
                ["tinker"] = (bot, _, cls) => TrySummon(bot, cls, 1000f)
            };
        }

        private bool ShouldEvaluate(CCSPlayerController bot)
        {
            var now = Server.CurrentTime;
            var handle = bot.Handle;

            if (!_nextBotEvaluation.TryGetValue(handle, out var next) || now >= next)
            {
                _nextBotEvaluation[handle] = now + 1.2f;
                return true;
            }

            return false;
        }

        private void DelayNextDecision(CCSPlayerController bot, float delaySeconds)
        {
            _nextBotEvaluation[bot.Handle] = Server.CurrentTime + delaySeconds;
        }

        private static float GetHealthFraction(CCSPlayerController player)
        {
            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || pawn.MaxHealth <= 0) return 1f;

            var value = pawn.Health / (float)pawn.MaxHealth;
            if (value < 0f) value = 0f;
            if (value > 1f) value = 1f;
            return value;
        }

        private static bool TryFindEnemy(CCSPlayerController bot, float range, out CCSPlayerController target)
        {
            target = null;
            var pawn = bot.PlayerPawn?.Value;
            if (pawn == null) return false;

            var origin = pawn.AbsOrigin;
            var rangeSq = range * range;
            var best = rangeSq;

            foreach (var candidate in Utilities.GetPlayers())
            {
                if (!candidate.IsAlive() || candidate.Team == bot.Team || candidate.Handle == bot.Handle) continue;
                var candidatePawn = candidate.PlayerPawn?.Value;
                if (candidatePawn == null) continue;

                var distSq = DistanceSquared(origin, candidatePawn.AbsOrigin);
                if (distSq < best)
                {
                    best = distSq;
                    target = candidate;
                }
            }

            return target != null;
        }

        private static float DistanceSquared(Vector a, Vector b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            var dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        private bool TryBurst(CCSPlayerController bot, WarcraftClass cls, float range, float panicHealth)
        {
            if (TryFindEnemy(bot, range, out _) || GetHealthFraction(bot) <= panicHealth)
            {
                cls.InvokeAbility(WarcraftPlayer.UltimateAbilityIndex);
                DelayNextDecision(bot, 4f);
                return true;
            }
            return false;
        }

        private bool TryMageDash(CCSPlayerController bot, WarcraftClass cls)
        {
            if (GetHealthFraction(bot) <= 0.45f)
            {
                cls.InvokeAbility(WarcraftPlayer.UltimateAbilityIndex);
                DelayNextDecision(bot, 6f);
                return true;
            }

            if (TryFindEnemy(bot, 650f, out var enemy))
            {
                FaceTarget(bot, enemy);
                cls.InvokeAbility(WarcraftPlayer.UltimateAbilityIndex);
                DelayNextDecision(bot, 6f);
                return true;
            }
            return false;
        }

        private bool TrySummon(CCSPlayerController bot, WarcraftClass cls, float enemyRange)
        {
            if (TryFindEnemy(bot, enemyRange, out _))
            {
                cls.InvokeAbility(WarcraftPlayer.UltimateAbilityIndex);
                DelayNextDecision(bot, 8f);
                return true;
            }
            return false;
        }

        private bool TryRevive(CCSPlayerController bot, WarcraftClass cls)
        {
            if (Utilities.GetPlayers().Any(p => p.Team == bot.Team && !p.PawnIsAlive && p.IsValid))
            {
                cls.InvokeAbility(WarcraftPlayer.UltimateAbilityIndex);
                DelayNextDecision(bot, 8f);
                return true;
            }
            return false;
        }

        private bool TryArrowstorm(CCSPlayerController bot, WarcraftClass cls)
        {
            if (!TryFindEnemy(bot, 1200f, out var enemy)) return false;

            FaceTarget(bot, enemy);
            cls.InvokeAbility(WarcraftPlayer.UltimateAbilityIndex);
            DelayNextDecision(bot, 6f);
            return true;
        }

        private bool TryLifeBreak(CCSPlayerController bot, WarcraftClass cls)
        {
            if (GetHealthFraction(bot) <= 0.35f) return false;
            if (!TryFindEnemy(bot, 550f, out var enemy)) return false;

            FaceTarget(bot, enemy);
            cls.InvokeAbility(WarcraftPlayer.UltimateAbilityIndex);
            DelayNextDecision(bot, 6f);
            return true;
        }

        private static void FaceTarget(CCSPlayerController bot, CCSPlayerController target)
        {
            var pawn = bot.PlayerPawn?.Value;
            var enemyPawn = target.PlayerPawn?.Value;
            if (pawn == null || enemyPawn == null) return;

            var dx = enemyPawn.AbsOrigin.X - pawn.AbsOrigin.X;
            var dy = enemyPawn.AbsOrigin.Y - pawn.AbsOrigin.Y;
            var dz = enemyPawn.AbsOrigin.Z - pawn.AbsOrigin.Z;

            var yaw = MathF.Atan2(dy, dx) * 180f / MathF.PI;
            var planar = MathF.Sqrt(dx * dx + dy * dy);
            var pitch = -MathF.Atan2(dz, planar) * 180f / MathF.PI;

            var look = new QAngle(pitch, yaw, 0);
            pawn.Teleport(null, look, null);
        }
    }
}
