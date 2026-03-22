using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API;
using System.Drawing;
using System;
using WarcraftPlugin.Helpers;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Timers;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;
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
        private CBeam _lazerDot;
        private Vector _target;
        private readonly CCSPlayerController _owner;

        internal float Angle { get; set; } = 0f;

        internal Drone(CCSPlayerController owner, Vector position)
        {
            _owner = owner;
            Position = position;
            Activate();
        }

        private void Activate()
        {
            Deactivate();
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
                _lazerDot = Warcraft.DrawLaserBetween(_turret.CalculatePositionInFront(new Vector(0, 30, 2)), _target, Color.FromArgb(15, 255, 0, 0), 0.2f, 0.2f);
            }
        }

        internal void EnemySpotted(CCSPlayerController enemy)
        {
            if (!IsFireRateCooldown)
            {
                var droneLevel = _owner.GetWarcraftPlayer().GetAbilityLevel(0);
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

        private void TryShootTarget(CCSPlayerController target, bool isRocket = false)
        {
            var targetPawn = target?.PlayerPawn?.Value;
            if (target == null || !target.IsAlive() || targetPawn == null || !targetPawn.IsValid)
                return;

            if (_turret != null && _turret.IsValid)
            {
                var playerCollison = targetPawn.CollisionBox();
                //playerCollison.Show(); //debug

                //check if we have a clear line of sight to target
                var turretMuzzle = _turret.CalculatePositionInFront(new Vector(0, 30, 2));
                var endPos = RayTracer.Trace(turretMuzzle, playerCollison.Center.ToVector(), false);

                //ensure trace has hit the players hitbox
                if (endPos != null && playerCollison.Contains(endPos))
                {
                    _target = endPos;

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
                        FireRocket(turretMuzzle, endPos);
                    }
                    else
                    {
                        Shoot(turretMuzzle, target);
                    }
                }
                else
                {
                    _target = null;
                }
            }
        }

        private void Shoot(Vector muzzle, CCSPlayerController target)
        {
            //particle effect from turret
            Warcraft.SpawnParticle(muzzle, "particles/weapons/cs_weapon_fx/weapon_muzzle_flash_assaultrifle.vpcf", 1);
            _turret.EmitSound("Weapon_M4A1.Silenced");

            //dodamage to target
            target.TakeDamage(_owner.GetWarcraftPlayer().GetAbilityLevel(0) * 1, _owner, KillFeedIcon.controldrone);
        }

        private void FireRocket(Vector muzzle, Vector endPos)
        {
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
