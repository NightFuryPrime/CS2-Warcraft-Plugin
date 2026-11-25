using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API;
using System.Drawing;
using System;
using WarcraftPlugin.Helpers;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Timers;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;
using WarcraftPlugin.Core;
using WarcraftPlugin.Diagnostics;
using WarcraftPlugin.Models;
using System.Collections.Generic;

namespace WarcraftPlugin.Summons
{
    internal class Drone
    {
        private CPhysicsPropMultiplayer _drone;
        private CDynamicProp _model;
        private CDynamicProp _turret;
        internal Vector Position { get; set; } = new(70, -70, 90);

        internal bool IsFireRateCooldown { get; set; } = false;
        private readonly float _fireRate = 2f;
        private Timer _fireRateTimer;
        private readonly List<Timer> _shotTimers = [];
        private readonly List<(CCSPlayerController Player, float DistanceSquared)> _scanCandidates = new(32);
        private CBeam _lazerDot;
        private Vector _target;
        private readonly CCSPlayerController _owner;
        private const float ScanRange = 800f;
        private const float ScanInterval = 0.5f;
        private const float TraceStartOffset = 40f;
        private float _nextScanTime;

        internal float Angle { get; set; } = 0f;

        internal static int GetRocketChance(int level)
        {
            return Math.Clamp(level, 0, 5) * 10;
        }

        internal Drone(CCSPlayerController owner, Vector position)
        {
            _owner = owner;
            Position = position;
            Activate();
        }

        private void Activate()
        {
            Deactivate();
            if (_owner?.IsValid != true)
            {
                PersistentLogger.Error(nameof(Drone), "Skipping drone activation because owner is invalid.");
                return;
            }

            var ownerPawn = _owner.PlayerPawn?.Value;
            if (ownerPawn == null || !ownerPawn.IsValid)
            {
                PersistentLogger.Error(nameof(Drone), $"Skipping drone activation because owner pawn is invalid for '{_owner?.PlayerName}'.");
                return;
            }

            //Spawn animation
            var droneSpawnAnimation = Warcraft.SpawnParticle(_owner.CalculatePositionInFront(Position), "particles/ui/ui_electric_gold.vpcf");
            if (droneSpawnAnimation != null)
            {
                droneSpawnAnimation.SetParent(ownerPawn);
            }

            //Create drone physics object
            _drone = Utilities.CreateEntityByName<CPhysicsPropMultiplayer>("prop_physics_multiplayer");
            if (_drone == null || !_drone.IsValid)
            {
                PersistentLogger.Error(nameof(Drone), "Failed to create drone physics prop.");
                return;
            }
            _drone.DispatchSpawn();
            WarcraftPlugin.Instance.AddTimer(0.0f, () =>
            {
                if (!_drone.IsValid) return;
                _drone.SetColor(Color.FromArgb(0, 255, 255, 255));
                WarcraftPlugin.Instance.DebugSetModel("[Drone] Body", _drone, "models/props/de_dust/hr_dust/dust_soccerball/dust_soccer_ball001.vmdl");
                _drone.SetModel("models/props/de_dust/hr_dust/dust_soccerball/dust_soccer_ball001.vmdl");
            });

            //Create drone body
            _model = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            if (_model == null || !_model.IsValid)
            {
                PersistentLogger.Error(nameof(Drone), "Failed to create drone body prop.");
                return;
            }
            _model.DispatchSpawn();
            WarcraftPlugin.Instance.AddTimer(0.0f, () =>
            {
                if (!_model.IsValid) return;
                WarcraftPlugin.Instance.DebugSetModel("[Drone] Model", _model, "models/props/de_dust/hr_dust/dust_soccerball/dust_soccer_ball001.vmdl");
                _model.SetModel("models/props/de_dust/hr_dust/dust_soccerball/dust_soccer_ball001.vmdl");
                _model.SetColor(Color.FromArgb(255, 0, 0, 0));
            });

            //Create drone turret
            _turret = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            if (_turret == null || !_turret.IsValid)
            {
                PersistentLogger.Error(nameof(Drone), "Failed to create drone turret prop.");
                return;
            }
            _turret.DispatchSpawn();
            WarcraftPlugin.Instance.AddTimer(0.0f, () =>
            {
                if (!_turret.IsValid) return;
                WarcraftPlugin.Instance.DebugSetModel("[Drone] Turret", _turret, "models/tools/bullet_hit_marker.vmdl");
                _turret.SetModel("models/tools/bullet_hit_marker.vmdl");
            });

            //Attach drone turret to body
            _turret.SetParent(_model, offset: new Vector(2, 2, 2), rotation: new QAngle(0, 310, 0));
            PersistentLogger.Breadcrumb(nameof(Drone), "Applying drone visual setup.", throttleMs: 0);
            var turretSkeleton = _turret.CBodyComponent?.SceneNode?.GetSkeletonInstance();
            if (turretSkeleton != null)
            {
                turretSkeleton.Scale = 0.8f;
            }
            _turret.SetColor(Color.FromArgb(255, 0, 0, 0));

            //Attach drone body to physics object
            _model.SetParent(_drone, rotation: new QAngle(175, 30, 0));
            var modelSkeleton = _model.CBodyComponent?.SceneNode?.GetSkeletonInstance();
            if (modelSkeleton != null)
            {
                modelSkeleton.Scale = 0.8f;
            }

            _drone.Teleport(_owner.CalculatePositionInFront(Position), ownerPawn.V_angle, new Vector(nint.Zero));
        }

        internal void Deactivate()
        {
            _target = null;

            _turret?.RemoveIfValid();
            _model?.RemoveIfValid();
            _drone?.RemoveIfValid();
            _lazerDot?.RemoveIfValid();
            _turret = null;
            _model = null;
            _drone = null;
            _lazerDot = null;
            foreach (var timer in _shotTimers)
            {
                timer?.Kill();
            }
            _shotTimers.Clear();
            _fireRateTimer?.Kill();
            _fireRateTimer = null;

            IsFireRateCooldown = false;
        }

        internal void Update()
        {
            if (_owner?.IsValid != true)
                return;

            var ownerPawn = _owner.PlayerPawn?.Value;
            if (!_owner.IsValid || ownerPawn == null || !ownerPawn.IsValid || _drone == null || !_drone.IsValid) return;
            var nextDronePosition = _owner.CalculatePositionInFront(Position);
            Vector velocity = Warcraft.CalculateTravelVelocity(_drone.AbsOrigin, nextDronePosition, 0.5f);
            _drone.Teleport(null, ownerPawn.V_angle, velocity);

            //Ensure drone is not stuck
            float droneDistanceToPlayer = (ownerPawn.AbsOrigin - _drone.AbsOrigin).Length();
            if (droneDistanceToPlayer > 500) _drone.Teleport(_owner.CalculatePositionInFront(Position), ownerPawn.V_angle, new Vector(nint.Zero));

            //Update laser to point at target
            if (_target != null)
            {
                if (_turret == null || !_turret.IsValid)
                    return;

                _lazerDot = Warcraft.DrawLaserBetween(_turret.CalculatePositionInFront(new Vector(0, 30, 2)), _target, Color.FromArgb(15, 255, 0, 0), 0.2f, 0.2f);
            }

            if (!IsFireRateCooldown && Server.CurrentTime >= _nextScanTime)
            {
                _nextScanTime = Server.CurrentTime + ScanInterval;
                ScanAndShoot();
            }
        }

        private void ScanAndShoot()
        {
            var ownerPawn = _owner?.PlayerPawn?.Value;
            if (_owner?.IsValid != true || ownerPawn == null || !ownerPawn.IsValid || _turret == null || !_turret.IsValid)
                return;

            _scanCandidates.Clear();
            var turretOrigin = _turret.AbsOrigin;
            var scanRangeSquared = ScanRange * ScanRange;
            foreach (var player in PlayerCache.GetPlayers())
            {
                if (player == null || !player.IsAlive() || player.Handle == _owner.Handle || player.AllyOf(_owner))
                    continue;

                var pawn = player.PlayerPawn?.Value;
                if (pawn == null || !pawn.IsValid)
                    continue;

                var dx = pawn.AbsOrigin.X - turretOrigin.X;
                var dy = pawn.AbsOrigin.Y - turretOrigin.Y;
                var dz = pawn.AbsOrigin.Z - turretOrigin.Z;
                var distanceSquared = dx * dx + dy * dy + dz * dz;
                if (distanceSquared > scanRangeSquared)
                    continue;

                var insertionIndex = _scanCandidates.Count;
                while (insertionIndex > 0 && _scanCandidates[insertionIndex - 1].DistanceSquared > distanceSquared)
                    insertionIndex--;
                _scanCandidates.Insert(insertionIndex, (player, distanceSquared));
            }

            foreach (var candidate in _scanCandidates)
            {
                if (TryShootTarget(candidate.Player))
                    return;
            }
        }

        internal void EnemySpotted(CCSPlayerController enemy)
        {
            var wcPlayer = _owner?.GetWarcraftPlayer();
            if (_owner?.IsValid != true || wcPlayer == null)
                return;

            if (!IsFireRateCooldown)
            {
                var droneLevel = wcPlayer.GetAbilityLevel(0);
                var timesToShoot = droneLevel + 3;

                TryShootTarget(enemy);

                for (var i = 0; i < timesToShoot; i++)
                {
                    // Only fire bullets, no rockets
                    var timer = WarcraftPlugin.Instance.AddTimer((float)(0.2 * i), () => TryShootTarget(enemy, false));
                    _shotTimers.Add(timer);
                }
            }
        }

        private bool TryShootTarget(CCSPlayerController target, bool isRocket = false)
        {
            var ownerPawn = _owner?.PlayerPawn?.Value;
            if (_owner?.IsValid != true || ownerPawn == null || !ownerPawn.IsValid || _owner.GetWarcraftPlayer() == null)
                return false;

            var targetPawn = target?.PlayerPawn?.Value;
            if (target == null || !target.IsAlive() || targetPawn == null || !targetPawn.IsValid)
                return false;

            if (_turret != null && _turret.IsValid)
            {
                var turretMuzzle = _turret.CalculatePositionInFront(new Vector(0, 30, 2));
                var targetPoint = targetPawn.EyePosition(-8);
                var traceStart = GetTraceStart(turretMuzzle, targetPoint);
                var trace = RayTracer.TraceLine(traceStart, targetPoint, ignoreEntity: ownerPawn);
                if (trace.Reaches(targetPawn.Handle))
                {
                    _target = targetPoint;

                    if (!IsFireRateCooldown)
                    {
                        //start fireing cooldown
                        IsFireRateCooldown = true;
                        _fireRateTimer = WarcraftPlugin.Instance.AddTimer(_fireRate, () =>
                        {
                            IsFireRateCooldown = false; _target = null;
                        });
                    }

                    if (isRocket)
                    {
                        FireRocket(turretMuzzle, targetPoint);
                    }
                    else
                    {
                        Shoot(turretMuzzle, target);
                    }
                    return true;
                }
                else
                {
                    PersistentLogger.Breadcrumb(nameof(Drone), $"Skipping drone shot after line-of-sight trace: {trace.State}.", throttleMs: 1000);
                    _target = null;
                }
            }

            return false;
        }

        private static Vector GetTraceStart(Vector muzzle, Vector targetPoint)
        {
            var dx = targetPoint.X - muzzle.X;
            var dy = targetPoint.Y - muzzle.Y;
            var dz = targetPoint.Z - muzzle.Z;
            var length = MathF.Max(1f, MathF.Sqrt(dx * dx + dy * dy + dz * dz));

            return muzzle.Clone().Add(
                x: dx / length * TraceStartOffset,
                y: dy / length * TraceStartOffset,
                z: dz / length * TraceStartOffset);
        }

        private void Shoot(Vector muzzle, CCSPlayerController target)
        {
            var wcPlayer = _owner?.GetWarcraftPlayer();
            if (_turret == null || !_turret.IsValid || wcPlayer == null || _owner?.IsValid != true)
                return;

            //particle effect from turret
            Warcraft.SpawnParticle(muzzle, "particles/weapons/cs_weapon_fx/weapon_muzzle_flash_assaultrifle.vpcf", 1);
            _turret.EmitSound("Weapon_M4A1.Silenced");

            //dodamage to target
            target.TakeDamage(wcPlayer.GetAbilityLevel(0) * 1, _owner, KillFeedIcon.controldrone);
        }

        private void FireRocket(Vector muzzle, Vector endPos)
        {
            if (_turret == null || !_turret.IsValid)
                return;

            var ownerPawn = _owner.PlayerPawn?.Value;
            if (ownerPawn == null || !ownerPawn.IsValid)
                return;

            PersistentLogger.Breadcrumb(nameof(Drone), "Firing drone rocket.", throttleMs: 0);
            var rocket = Utilities.CreateEntityByName<CHEGrenadeProjectile>("hegrenade_projectile");
            if (rocket == null || !rocket.IsValid)
                return;

            Vector velocity = Warcraft.CalculateTravelVelocity(_turret.AbsOrigin, endPos, 1);

            rocket.Teleport(muzzle, rocket.AbsRotation, velocity);
            rocket.DispatchSpawn();
            if (_owner.PlayerPawn != null && _owner.PlayerPawn.IsValid)
            {
                Schema.SetSchemaValue(rocket.Handle, "CBaseGrenade", "m_hThrower", _owner.PlayerPawn.Raw); //Fixes killfeed
            }

            //Rocket popping out the tube
            Warcraft.SpawnParticle(rocket.AbsOrigin, "particles/explosions_fx/explosion_hegrenade_smoketrails.vpcf", 1);
            rocket.EmitSound("Weapon_Nova.Pump", volume: 0.5f);

            rocket.AcceptInput("InitializeSpawnFromWorld");

            rocket.Damage = 40;
            rocket.DmgRadius = 200;
        }
    }
}
