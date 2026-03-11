using System;
using System.Drawing;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using WarcraftPlugin.Helpers;
using CounterStrikeSharp.API;
using System.Collections.Generic;
using System.Linq;
using g3;
using WarcraftPlugin.Models;
using WarcraftPlugin.Core.Effects;
using CounterStrikeSharp.API.Modules.Timers;
using WarcraftPlugin.Summons;
using WarcraftPlugin.Events.ExtendedEvents;
using CounterStrikeSharp.API.Modules.Entities;

namespace WarcraftPlugin.Classes
{
    internal class Necromancer : WarcraftClass
    {
        public override string DisplayName => "Necromancer";
        public override Color DefaultColor => Color.Black;

        public override List<IWarcraftAbility> Abilities =>
        [
            new WarcraftAbility("Life Drain", "Heal for 6/12/18/24/30% of damage dealt."),
            new WarcraftAbility("Poison Cloud", "Smoke cloud deals 2/4/6/8/10 damage per tick."),
            new WarcraftAbility("Splintered Soul", "16/32/48/64/80% chance to cheat death."),
            new WarcraftCooldownAbility("Raise Dead", "Summon a horde of undead chicken to fight for you.", 50f)
        ];

        private readonly List<Zombie> _zombies = new();
        private const int _maxZombies = 10;
        private bool _hasCheatedDeath = true;
        private Timer _zombieUpdateTimer;
        private const string PoisonGrenadeName = "Poison cloud";

        public override void Register()
        {
            RegisterHooks()
                .Hook<EventPlayerSpawn>(PlayerSpawn)
                .Hook<EventRoundEnd>(RoundEnd)
                .Hook<EventRoundStart>(RoundStart)
                .Hook<EventPlayerDeath>(PlayerDeath)
                .Hook<EventPlayerHurtOther>(PlayerHurtOther)
                .Hook<EventGrenadeThrown>(GrenadeThrown)
                .Hook<EventSmokegrenadeDetonate>(SmokegrenadeDetonate)
                .Hook<EventSpottedEnemy>(SpottedPlayer)
                .Hook<EventItemEquip>(ItemEquip);

            RegisterAbilities()
                .Bind(3, Ultimate);
        }

        public override void OnPlayerSpawned()
        {
            ResetPoisonSmokes();
            if (WarcraftPlayer.GetAbilityLevel(1) > 0)
            {
                var decoy = Player.GiveWeapon<CDecoyGrenade>("weapon_smokegrenade");
                if (decoy == null) return;
                decoy.AttributeManager.Item.CustomName = PoisonGrenadeName;
            }
        }

        private void PlayerSpawn(EventPlayerSpawn spawn) => OnPlayerSpawned();

        private void PlayerDeath(EventPlayerDeath death)
        {
            KillZombies();
            ResetPoisonSmokes();
            if (WarcraftPlayer.GetAbilityLevel(2) == 0) return;

            var pawn = Player?.PlayerPawn?.Value;
            if (pawn != null && !_hasCheatedDeath && pawn.Health <= 0)
            {
                if (Warcraft.RollAbilityCheck(WarcraftPlayer.GetAbilityLevel(2), 80))
                {
                    _hasCheatedDeath = true;
                    WarcraftPlugin.Instance.AddTimer(2f, () =>
                    {
                        if (Player == null || !Player.IsValid) return;
                        Player.PrintToChat(" " + Localizer["necromancer.cheatdeath"]);
                        Player.Respawn();

                        Server.NextFrame(() => {
                            if (Player.IsAlive()) {
                                Player.SetHp(1);
                                var respawnPawn = Player.PlayerPawn.Value;
                                if (respawnPawn != null) {
                                    Warcraft.SpawnParticle(respawnPawn.AbsOrigin, "particles/explosions_fx/explosion_smokegrenade_init.vpcf", 2);
                                }
                                Player.EmitSound("Player.BecomeGhost", volume: 0.5f);
                            }
                        });
                    });
                }
            }
        }

        private void SmokegrenadeDetonate(EventSmokegrenadeDetonate detonate)
        {
            if (WarcraftPlayer.GetAbilityLevel(1) > 0)
            {
                new PoisonCloudEffect(Player, 13, new Vector(detonate.X, detonate.Y, detonate.Z)).Start();
            }
        }

        private void GrenadeThrown(EventGrenadeThrown thrown)
        {
            if (WarcraftPlayer.GetAbilityLevel(1) > 0 && thrown.Weapon == "smokegrenade")
            {
                var smokeGrenade = Utilities.FindAllEntitiesByDesignerName<CSmokeGrenadeProjectile>("smokegrenade_projectile")
                    .Where(x => x.Thrower.Index == Player.PlayerPawn.Index)
                    .OrderByDescending(x => x.CreateTime).FirstOrDefault();

                if (smokeGrenade == null) return;

                var smokeColor = Color.FromArgb(100 - (int)((float)WarcraftPlayer.GetAbilityLevel(1) * (100 / 5)), 255, 0); //slight red shift
                smokeGrenade.SmokeColor.X = smokeColor.R;
                smokeGrenade.SmokeColor.Y = smokeColor.G;
                smokeGrenade.SmokeColor.Z = smokeColor.B;
            }
        }

        private void PlayerHurtOther(EventPlayerHurtOther hurt)
        {
            var attacker = hurt.Attacker;
            var victim = hurt.Userid;
            if (attacker == null || attacker.UserId != Player.UserId || victim == null || !victim.IsAlive()) return;

            var attackerPawn = attacker.PlayerPawn?.Value;
            if (attackerPawn != null && attackerPawn.Health < attackerPawn.MaxHealth)
            {
                var victimPawn = victim.PlayerPawn.Value;
                if (victimPawn != null) {
                    Warcraft.SpawnParticle(victimPawn.AbsOrigin.Clone().Add(z: 30), "particles/critters/chicken/chicken_impact_burst_zombie.vpcf");
                }
                var healthDrained = hurt.DmgHealth * ((float)WarcraftPlayer.GetAbilityLevel(0) / WarcraftPlugin.MaxSkillLevel * 0.3f);
                var healAmount = Player.Heal((int)healthDrained, GetAbility(0).DisplayName);
                victim.PrintToChat($" {Localizer["necromancer.lifedrain", Player.GetRealPlayerName(), healAmount]}");
            }
        }

        private void RoundStart(EventRoundStart start)
        {
            KillZombies();
            _hasCheatedDeath = false;
        }

        private void RoundEnd(EventRoundEnd end)
        {
            KillZombies();
            ResetPoisonSmokes();
        }

        private void KillZombies()
        {
            _zombieUpdateTimer?.Kill();

            foreach (var zombie in _zombies)
            {
                zombie.Kill();
            }

            _zombies.Clear();
        }

        private void Ultimate()
        {
            if (WarcraftPlayer.GetAbilityLevel(3) < 1 || !IsAbilityReady(3)) return;

            RaiseDead();
            StartCooldown(3);
        }

        private void RaiseDead()
        {
            KillZombies();

            Player.EmitSound("Player.BecomeGhost", volume: 0.5f);

            for (int i = 0; i < _maxZombies; i++)
            {
                _zombies.Add(new Zombie(Player));
            }

            _zombieUpdateTimer?.Kill();

            _zombieUpdateTimer = WarcraftPlugin.Instance.AddTimer(0.1f, () =>
            {
                var hasValidZombies = false;
                var zombieCount = _zombies.Count;
                var zombieIndex = 0;
                foreach (var zombie in _zombies)
                {
                    if (zombie.Entity.IsValid)
                    {
                        zombieIndex++;
                        zombie.FavouritePosition = (zombieIndex * 100) / zombieCount;
                        zombie.Update();
                        hasValidZombies = true;
                    }
                }

                if (!hasValidZombies)
                {
                    _zombieUpdateTimer?.Kill();
                    _zombies.Clear();
                }
            }, TimerFlags.REPEAT);
        }

        private void SpottedPlayer(EventSpottedEnemy enemy)
        {
            foreach (var zombie in _zombies)
            {
                if (zombie.Entity.IsValid)
                    zombie.SetEnemy(enemy.UserId);
            }
        }

        private void ItemEquip(EventItemEquip equip)
        {
            if (equip.Userid?.Handle != Player.Handle) return;
            if (WarcraftPlayer.GetAbilityLevel(1) <= 0) return;

            var activeWeapon = Player?.PlayerPawn?.Value?.WeaponServices?.ActiveWeapon?.Value;
            if (activeWeapon?.DesignerName == "weapon_smokegrenade")
            {
                activeWeapon.AttributeManager.Item.CustomName = PoisonGrenadeName;
            }
        }

        private static void ResetPoisonSmokes()
        {
            foreach (var weapon in Utilities.FindAllEntitiesByDesignerName<CBasePlayerWeapon>("weapon_smokegrenade"))
            {
                if (weapon?.AttributeManager?.Item?.CustomName == PoisonGrenadeName)
                {
                    weapon.AttributeManager.Item.CustomName = string.Empty;
                }
            }
        }

        internal class PoisonCloudEffect : WarcraftEffect
        {
            readonly int _cloudHeight = 100;
            readonly int _cloudWidth = 260;
            private Box3d _hurtBox;
            private readonly Vector _cloudPos;
            private readonly int _damagePerTick;

            public PoisonCloudEffect(CCSPlayerController owner, float duration, Vector cloudPos)
                : base(owner, duration, destroyOnDeath: false)
            {
                _cloudPos = cloudPos;
                _damagePerTick = Math.Max(0, owner.GetWarcraftPlayer()?.GetAbilityLevel(1) * 2 ?? 0);
            }

            public override void OnStart()
            {
                var hurtBoxPoint = _cloudPos.With(z: _cloudPos.Z + _cloudHeight / 2);
                _hurtBox = Warcraft.CreateBoxAroundPoint(hurtBoxPoint, _cloudWidth, _cloudWidth, _cloudHeight);
                //_hurtBox.Show(duration: Duration); //Debug
            }

            public override void OnTick()
            {
                //Find players within area
                var playersInHurtZone = Utilities.GetPlayers()
                    .Where(x => {
                        if (!x.PawnIsAlive || x.AllyOf(Owner)) return false;
                        var pawn = x.PlayerPawn?.Value;
                        return pawn != null && _hurtBox.Contains(pawn.AbsOrigin.Clone().Add(z: 20));
                    })
                    .ToList();

                if (playersInHurtZone.Count == 0)
                {
                    return;
                }

                foreach (var player in playersInHurtZone)
                {
                    player.TakeDamage(_damagePerTick, Owner, KillFeedIcon.prop_exploding_barrel);
                }
            }

            public override void OnFinish(){}
        }
    }
}
