using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;
using CounterStrikeSharp.API.Modules.Memory;
using WarcraftPlugin.Helpers;
using WarcraftPlugin.Models;
using System.Drawing;
using WarcraftPlugin.Core.Effects;
using System.Collections.Generic;
using WarcraftPlugin.Events.ExtendedEvents;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using System;
using System.Linq;

namespace WarcraftPlugin.Classes
{
    internal class Barbarian : WarcraftClass
    {
        public override string DisplayName => "Barbarian";
        public override DefaultClassModel DefaultModel => new()
        {
            TModel = "characters/models/tm_phoenix_heavy/tm_phoenix_heavy.vmdl",
            CTModel = "characters/models/ctm_heavy/ctm_heavy.vmdl"
        };

        public override List<string> PreloadResources =>
        [
            "models/cs_italy/props/barrel/italy_barrel_wood_1.vmdl"
        ];

        public override Color DefaultColor => Color.Brown;

        public override List<IWarcraftAbility> Abilities =>
        [
            new WarcraftAbility("Carnage", "Deal an extra 5/10/15/20/25 damage with shotguns."),
            new WarcraftAbility("Battle-Hardened", "Increase your health by 20/40/60/80/100."),
            new WarcraftAbility("Throwing Axe", "Chance to throw an exploding barrel when firing (around 3–13% per shot depending on weapon)."),
            new WarcraftCooldownAbility("Bloodlust", "For 10s gain infinite ammo, 30% more speed and constant health regen.", 50f)
        ];

        private readonly int _battleHardenedHealthMultiplier = 20;
        private readonly float _bloodlustLength = 10;

        public override void Register()
        {
            RegisterHooks()
                .Hook<EventPlayerHurtOther>(PlayerHurtOther)
                .Hook<EventPlayerSpawn>(PlayerSpawn)
                .Hook<EventWeaponFire>(PlayerShoot);

            RegisterAbilities()
                .Bind(3, Ultimate);
        }

        private void PlayerShoot(EventWeaponFire @event)
        {
            var pawn = Player?.PlayerPawn?.Value;
            if (pawn == null) return;

            var activeWeapon = pawn.WeaponServices?.ActiveWeapon.Value;
            if (activeWeapon != null && activeWeapon.IsValid && activeWeapon.VData != null)
            {
                var maxClip = activeWeapon.VData.MaxClip1;
                if (maxClip == 0) return;

                var maxChance = 400 / maxClip; // The bigger the mag, the lower the chance, to avoid negev spam

                if (Warcraft.RollAbilityCheck(WarcraftPlayer.GetAbilityLevel(2), maxChance))
                {
                    new ThrowingAxeEffect(Player, 2).Start();
                }
            }
        }

        public override void OnPlayerSpawned()
        {
            if (WarcraftPlayer.GetAbilityLevel(1) > 0)
            {
                Server.NextFrame(() =>
                {
                    if (!Player.IsAlive()) return;
                    Player.SetHp(100 + WarcraftPlayer.GetAbilityLevel(1) * _battleHardenedHealthMultiplier);
                    var pawn = Player.PlayerPawn.Value;
                    if (pawn != null) pawn.MaxHealth = pawn.Health;
                });
            }
        }

        private void PlayerSpawn(EventPlayerSpawn @event) => OnPlayerSpawned();

        private void Ultimate()
        {
            if (WarcraftPlayer.GetAbilityLevel(3) < 1 || !IsAbilityReady(3)) return;

            new BloodlustEffect(Player, _bloodlustLength).Start();
            StartCooldown(3);
        }

        private void PlayerHurtOther(EventPlayerHurtOther @event)
        {
            var attacker = @event.Attacker;
            var victim = @event.Userid;
            if (attacker == null || attacker.UserId != Player.UserId || victim == null || !victim.IsAlive()) return;

            var carnageLevel = WarcraftPlayer.GetAbilityLevel(0);

            if (carnageLevel > 0 && WeaponTypes.Shotguns.Contains(@event.Weapon))
            {
                @event.AddBonusDamage(carnageLevel * 5, abilityName: GetAbility(0).DisplayName);
                Warcraft.SpawnParticle(victim.PlayerPawn.Value.AbsOrigin.With(z: victim.PlayerPawn.Value.AbsOrigin.Z + 60), "particles/blood_impact/blood_impact_basic.vpcf");
                victim.EmitSound("Flesh.ImpactHard", volume: 0.5f);
            }
        }
    }

    internal class ThrowingAxeEffect(CCSPlayerController owner, float duration) : WarcraftEffect(owner, duration)
    {
        private CHEGrenadeProjectile _throwingAxe;
        private bool _hasDetonated;
        private const float BarrelDamage = 20f;
        private const float BarrelRadius = 125f;
        private Vector _lastKnownOrigin = new();

        public override void OnStart()
        {
            var pawn = Owner?.PlayerPawn?.Value;
            if (pawn == null) { Destroy(); return; }

            _throwingAxe = Utilities.CreateEntityByName<CHEGrenadeProjectile>("hegrenade_projectile");
            if (_throwingAxe == null) { Destroy(); return; }

            Vector velocity = Owner.CalculateVelocityAwayFromPlayer(1800);

            var rotation = new QAngle(0, pawn.GetEyeAngles().Y + 90, 0);

            _throwingAxe.Teleport(Owner.CalculatePositionInFront(10, 60), rotation, velocity);
            _throwingAxe.DispatchSpawn();
            WarcraftPlugin.Instance.DebugSetModel("[Barbarian] ThrowingAxe", _throwingAxe, "models/cs_italy/props/barrel/italy_barrel_wood_1.vmdl");
            _throwingAxe.SetModel("models/cs_italy/props/barrel/italy_barrel_wood_1.vmdl");
            Schema.SetSchemaValue(_throwingAxe.Handle, "CBaseGrenade", "m_hThrower", Owner.PlayerPawn.Raw); //Fixes killfeed

            _lastKnownOrigin = _throwingAxe.AbsOrigin.Clone();

            _throwingAxe.AcceptInput("InitializeSpawnFromWorld");
            _throwingAxe.Damage = BarrelDamage;
            _throwingAxe.DmgRadius = BarrelRadius;
            _throwingAxe.DetonateTime = float.MaxValue;

            Owner.EmitSound("Door.wood_full_open", volume: 0.5f);
        }

        public override void OnTick()
        {
            if (_hasDetonated) return;

            if (_throwingAxe == null || !_throwingAxe.IsValid)
            {
                TriggerExplosion();
                Destroy();
                return;
            }

            _lastKnownOrigin = _throwingAxe.AbsOrigin.Clone();

            if ((_throwingAxe?.HasEverHitEnemy ?? false) || HasTouchedWorld())
            {
                TriggerExplosion();
                Destroy();
            }
        }

        public override void OnFinish()
        {
            TriggerExplosion();
        }

        private bool HasTouchedWorld()
        {
            if (_throwingAxe == null || !_throwingAxe.IsValid)
                return false;

            bool touched = false;

            try
            {
                touched = Schema.GetSchemaValue<bool>(_throwingAxe.Handle, "CBaseGrenade", "m_bHasTouchedWorld");
            }
            catch { }

            if (!touched)
            {
                try
                {
                    var flags = Schema.GetSchemaValue<int>(_throwingAxe.Handle, "CBaseEntity", "m_fFlags");
                    touched = (flags & (int)PlayerFlags.FL_ONGROUND) != 0;
                }
                catch { }
            }

            if (!touched)
            {
                touched = _throwingAxe.AbsVelocity.Length() <= 40f;
            }

            return touched;
        }

        private void TriggerExplosion()
        {
            if (_hasDetonated)
                return;

            _hasDetonated = true;

            var axe = _throwingAxe;
            _throwingAxe = null;

            Vector origin = _lastKnownOrigin?.Clone();
            if (axe != null && axe.IsValid)
            {
                origin = axe.AbsOrigin.Clone();
            }
            origin ??= Owner?.PlayerPawn?.Value?.AbsOrigin.Clone() ?? new Vector();

            var attacker = Owner?.IsValid == true ? Owner : null;
            if (attacker != null)
            {
                // Spawn explosion particles and sound at the origin
                Warcraft.SpawnParticle(origin, "particles/explosions_fx/explosion_hegrenade_brief.vpcf");
                Warcraft.EmitSound(attacker, "BaseGrenade.Explode");

                var victims = Utilities.GetPlayers().Where(p =>
                    p.IsAlive() &&
                    !p.AllyOf(attacker) &&
                    p.PlayerPawn?.Value != null &&
                    Distance(p.PlayerPawn.Value.AbsOrigin, origin) <= BarrelRadius).ToList();

                foreach (var victim in victims)
                {
                    victim.TakeDamage(BarrelDamage, attacker, KillFeedIcon.prop_exploding_barrel);
                    attacker.PrintToChat($" {ChatColors.Gold}[Barrel]{ChatColors.Default} {victim.GetRealPlayerName()} -{BarrelDamage} HP");
                    victim.PrintToChat($" {ChatColors.Red}[Barrel]{ChatColors.Default} -{BarrelDamage} HP");
                }
            }

            // COMPLETELY REMOVE the barrel/grenade entity to prevent floating remnants
            if (axe != null && axe.IsValid)
            {
                axe.Remove();
            }
        }

        private static float Distance(Vector origin, Vector target)
        {
            var dx = origin.X - target.X;
            var dy = origin.Y - target.Y;
            var dz = origin.Z - target.Z;
            return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }

    internal class BloodlustEffect(CCSPlayerController owner, float duration) : WarcraftEffect(owner, duration)
    {
        private const float _maxSize = 1.1f;

        public override void OnStart()
        {
            if (!Owner.IsAlive()) return;
            Owner.AdrenalineSurgeEffect(Duration);
            var pawn = Owner.PlayerPawn.Value;
            if (pawn != null)
            {
                pawn.VelocityModifier = 1.3f;
                pawn.SetColor(Color.IndianRed);
            }
            Owner.EmitSound("BaseGrenade.JumpThrowM", volume: 0.5f);
        }

        public override void OnTick()
        {
            if (!Owner.IsAlive()) return;

            var pawn = Owner.PlayerPawn.Value;
            if (pawn == null) return;

            //Refill ammo
            var activeWeapon = pawn.WeaponServices?.ActiveWeapon?.Value;
            if (activeWeapon != null)
            {
                var vdata = activeWeapon.GetVData<CBasePlayerWeaponVData>();
                if (vdata != null) activeWeapon.Clip1 = vdata.MaxClip1;
            }

            //Regenerate health
            if (pawn.Health < pawn.MaxHealth)
            {
                Owner.SetHp(pawn.Health + 1);
            }

            //Rage growth spurt
            var skeleton = pawn.CBodyComponent?.SceneNode?.GetSkeletonInstance();
            if (skeleton != null && skeleton.Scale < _maxSize)
            {
                pawn.SetScale(skeleton.Scale + 0.01f);
            }
        }

        public override void OnFinish()
        {
            if (!Owner.IsAlive()) return;

            var pawn = Owner.PlayerPawn.Value;
            pawn.SetColor(Color.White);
            pawn.VelocityModifier = 1f;
            pawn.SetScale(1);
        }
    }
}
