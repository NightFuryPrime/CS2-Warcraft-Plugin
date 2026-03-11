using System;
using System.Drawing;
using CounterStrikeSharp.API.Core;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;
using CounterStrikeSharp.API;
using WarcraftPlugin.Helpers;
using WarcraftPlugin.Models;
using System.Linq;
using WarcraftPlugin.Core.Effects;
using System.Collections.Generic;
using WarcraftPlugin.Events.ExtendedEvents;
using CounterStrikeSharp.API.Modules.Entities;

namespace WarcraftPlugin.Classes
{
    internal class Mage : WarcraftClass
    {
        public override string DisplayName => "Mage";
        public override Color DefaultColor => Color.Blue;

        public override List<string> PreloadResources =>
        [
            "models/weapons/w_muzzlefireshape.vmdl",
            "models/anubis/structures/pillar02_base01.vmdl"
        ];

        public override List<IWarcraftAbility> Abilities =>
        [
            new WarcraftAbility("Fireball", "Infuses molotovs with fire magic, causing a huge explosion on impact (40/80/120/160/200 damage)."),
            new WarcraftAbility("Ice Beam", "5/10/15/20/25% chance to freeze enemies in place."),
            new WarcraftAbility("Mana Shield", "Regenerates 1 armor every 5/2.5/1.7/1.3/1s."),
            new WarcraftCooldownAbility("Arcane Dash", "Dash rapidly in the direction you're aiming.", 20f)
        ];

        private static readonly float[] ManaShieldIntervals = [0f, 5f, 2.5f, 1.7f, 1.3f, 1f];
        private const string FireballCustomName = "Fireball";
        private const int ArcaneDashForce = 1800;

        public override void Register()
        {
            RegisterHooks()
                .Hook<EventPlayerHurtOther>(PlayerHurtOther)
                .Hook<EventMolotovDetonate>(MolotovDetonate)
                .Hook<EventPlayerSpawn>(PlayerSpawn)
                .Hook<EventGrenadeThrown>(GrenadeThrown)
                .Hook<EventItemEquip>(ItemEquip);

            RegisterAbilities()
                .Bind(3, Ultimate);
        }

        private void PerformArcaneDash()
        {
            if (WarcraftPlayer.GetAbilityLevel(3) < 1 || !IsAbilityReady(3))
                return;

            var pawn = Player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;

            var dashVelocity = Player.CalculateVelocityAwayFromPlayer(ArcaneDashForce);
            pawn.AbsVelocity.X = dashVelocity.X;
            pawn.AbsVelocity.Y = dashVelocity.Y;
            pawn.AbsVelocity.Z = Math.Max(pawn.AbsVelocity.Z, dashVelocity.Z + 150f);

            StartCooldown(3);
            Player.DropWeaponByDesignerName("weapon_c4");

            Player.EmitSound("UIPanorama.equip_musicKit", volume: 0.5f);
            var origin = pawn.AbsOrigin.Clone();
            Warcraft.SpawnParticle(origin.Clone().Add(z: 20), "particles/ui/ui_electric_exp_glow.vpcf", 3);
            Warcraft.SpawnParticle(origin, "particles/explosions_fx/explosion_smokegrenade_distort.vpcf", 2);
            Warcraft.SpawnParticle(origin.Clone().Add(z: 20), "particles/ui/ui_electric_exp_glow.vpcf", 3);
            Warcraft.SpawnParticle(origin, "particles/explosions_fx/explosion_smokegrenade_distort.vpcf", 2);
        }

        private void Ultimate()
        {
            if (WarcraftPlayer.GetAbilityLevel(3) < 1 || !IsAbilityReady(3)) return;
            PerformArcaneDash();
        }

        public override void OnPlayerSpawned()
        {
            ResetFireballMolotovs();
            //Mana shield
            var manaShieldLevel = WarcraftPlayer.GetAbilityLevel(2);
            if (manaShieldLevel > 0)
            {
                var intervalIndex = Math.Clamp(manaShieldLevel, 1, WarcraftPlugin.MaxSkillLevel);
                var regenArmorRate = ManaShieldIntervals[intervalIndex];
                new ManaShieldEffect(Player, regenArmorRate).Start();
            }

            //Fireball
            if (WarcraftPlayer.GetAbilityLevel(0) > 0)
            {
                var decoy = Player.GiveWeapon<CDecoyGrenade>("weapon_molotov");
                if (decoy == null) return;
                decoy.AttributeManager.Item.CustomName = FireballCustomName;
            }
        }

        private void PlayerSpawn(EventPlayerSpawn @event) => OnPlayerSpawned();

        private void PlayerHurtOther(EventPlayerHurtOther @event)
        {
            var attacker = @event.Attacker;
            var victim = @event.Userid;
            if (attacker == null || attacker.UserId != Player.UserId || victim == null || !victim.IsAlive()) return;

            if (Warcraft.RollAbilityCheck(WarcraftPlayer.GetAbilityLevel(1), 25))
            {
                new FreezeEffect(Player, 1.0f, victim).Start();
            }
        }

        private void GrenadeThrown(EventGrenadeThrown thrown)
        {
            if (WarcraftPlayer.GetAbilityLevel(0) > 0 && thrown.Weapon == "molotov")
            {
                var throwerPawn = Player?.PlayerPawn?.Value;
                if (throwerPawn == null) return;

                var molotov = Utilities.FindAllEntitiesByDesignerName<CMolotovProjectile>("molotov_projectile")
                    .Where(x => x.Thrower.Index == throwerPawn.Index)
                    .OrderByDescending(x => x.CreateTime).FirstOrDefault();

                if (molotov == null) return;

                WarcraftPlugin.Instance.DebugSetModel("[Mage] Fireball", molotov, "models/weapons/w_muzzlefireshape.vmdl");
                molotov.SetModel("models/weapons/w_muzzlefireshape.vmdl");
                molotov.SetColor(Color.OrangeRed);

                var particle = Warcraft.SpawnParticle(molotov.AbsOrigin, "particles/inferno_fx/molotov_fire01.vpcf");
                particle.SetParent(molotov);

                Vector velocity = Player.CalculateVelocityAwayFromPlayer(1800);
                molotov.Teleport(Player.CalculatePositionInFront(10, 60), molotov.AbsRotation, velocity);

            }
        }

        private void MolotovDetonate(EventMolotovDetonate @event)
        {
            var abilityLevel = WarcraftPlayer.GetAbilityLevel(0);
            if (abilityLevel <= 0) return;

            var baseDamage = abilityLevel * 40f;
            var radius = abilityLevel * 100f;
            var origin = new Vector(@event.X, @event.Y, @event.Z);
            var enemies = Utilities.GetPlayers()
                .Where(x => x.IsAlive() && !x.AllyOf(Player))
                .ToList();

            foreach (var enemy in enemies)
            {
                var pawn = enemy.PlayerPawn?.Value;
                if (pawn == null) continue;

                var dx = pawn.AbsOrigin.X - origin.X;
                var dy = pawn.AbsOrigin.Y - origin.Y;
                var dz = pawn.AbsOrigin.Z - origin.Z;
                var distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);

                if (distance > radius) continue;

                var falloff = 1f - (distance / radius);
                var damage = Math.Max(5, (int)(baseDamage * falloff));
                enemy.TakeDamage(damage, Player, KillFeedIcon.molotov);
            }

            Warcraft.SpawnParticle(origin.Clone().Add(z: 10), "particles/survival_fx/gas_cannister_impact.vpcf");
            Warcraft.SpawnParticle(origin, "particles/ui/ui_electric_exp_glow.vpcf");
        }

        private void ItemEquip(EventItemEquip equip)
        {
            if (equip.Userid?.Handle != Player.Handle) return;
            if (WarcraftPlayer.GetAbilityLevel(0) <= 0) return;

            var activeWeapon = Player?.PlayerPawn?.Value?.WeaponServices?.ActiveWeapon?.Value;
            if (activeWeapon?.DesignerName == "weapon_molotov")
            {
                activeWeapon.AttributeManager.Item.CustomName = FireballCustomName;
            }
        }

        private static void ResetFireballMolotovs()
        {
            foreach (var weapon in Utilities.FindAllEntitiesByDesignerName<CBasePlayerWeapon>("weapon_molotov"))
            {
                if (weapon?.AttributeManager?.Item?.CustomName == FireballCustomName)
                {
                    weapon.AttributeManager.Item.CustomName = string.Empty;
                }
            }
        }
    }

    internal class ManaShieldEffect(CCSPlayerController owner, float onTickInterval) : WarcraftEffect(owner, onTickInterval: onTickInterval)
    {
        public override void OnStart()
        {
            // Fix: Check if pawn exists before accessing properties
            if (!Owner.IsValid || Owner.PlayerPawn?.Value == null) return;

            if (Owner.PlayerPawn.Value.ArmorValue == 0)
            {
                Owner.GiveWeapon<CBasePlayerWeapon>("item_assaultsuit");
                Owner.SetArmor(1);
            }
        }
        public override void OnTick()
        {
            if (!Owner.IsAlive()) return;
            var pawn = Owner.PlayerPawn.Value;
            if (pawn == null) return;

            if (pawn.ArmorValue < 100)
            {
                Owner.SetArmor(pawn.ArmorValue + 1);
            }
        }
        public override void OnFinish() { }
    }

    internal class FreezeEffect(CCSPlayerController owner, float duration, CCSPlayerController target) : WarcraftEffect(owner, duration)
    {
        public override void OnStart()
        {
            target.PrintToChat(" " + Localizer["mage.frozen"]);
            var targetPlayerModel = target.PlayerPawn.Value;

            targetPlayerModel.VelocityModifier = targetPlayerModel.VelocityModifier / 2;

            Warcraft.DrawLaserBetween(Owner.EyePosition(-10), target.EyePosition(-10), Color.Cyan);
            targetPlayerModel.SetColor(Color.Cyan);
        }
        public override void OnTick() { }
        public override void OnFinish()
        {
            if (target.IsValid && target.PawnIsAlive)
            {
                target.PlayerPawn.Value.SetColor(Color.White);
            }
        }
    }
}
