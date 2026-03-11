using System;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using WarcraftPlugin.Helpers;
using CounterStrikeSharp.API;
using System.Linq;
using WarcraftPlugin.Models;
using System.Drawing;
using System.Collections.Generic;
using WarcraftPlugin.Events.ExtendedEvents;
using WarcraftPlugin.Core.Effects;

namespace WarcraftPlugin.Classes
{
    internal class Paladin : WarcraftClass
    {
        private bool _hasUsedDivineResurrection = false;
        private const double DivineResurrectionChance = 0.8;

        public override string DisplayName => "Paladin";
        public override Color DefaultColor => Color.Yellow;

        public override List<IWarcraftAbility> Abilities =>
        [
            new WarcraftAbility("Healing Aura", "Heal allies within 200/300/400/500/600 units for 1/2/3/4/5 HP every few seconds."),
            new WarcraftAbility("Holy Shield", "Gain an additional 20/40/60/80/100 armor."),
            new WarcraftAbility("Smite", "15/30/45/60/75% chance to strip enemy armor for 5/10/15/20/25 points."),
            new WarcraftCooldownAbility("Divine Resurrection", "80% chance to instantly revive a fallen ally. Cooldown: 60s", 60f)
        ];

        public override void Register()
        {
            RegisterHooks()
                .Hook<EventPlayerSpawn>(PlayerSpawn)
                .Hook<EventPlayerHurtOther>(PlayerHurtOther)
                .Hook<EventRoundStart>(RoundStart);

            RegisterAbilities()
                .Bind(3, Ultimate);
        }

        private void RoundStart(EventRoundStart start)
        {
            _hasUsedDivineResurrection = false;
        }

        public override void OnPlayerSpawned()
        {
            if (WarcraftPlayer.GetAbilityLevel(0) > 0)
            {
                new HealingAuraEffect(Player, 5f).Start();
            }

            if (WarcraftPlayer.GetAbilityLevel(1) > 0)
            {
                Player.GiveWeapon<CBasePlayerWeapon>("item_assaultsuit");
                var armorBonus = WarcraftPlayer.GetAbilityLevel(1) * 20;
                Player.SetArmor(100 + armorBonus);
                Player.PrintToChat($" {ChatColors.Blue}+{armorBonus} armor {ChatColors.Gold}[{GetAbility(1).DisplayName}]");
            }
        }

        private void PlayerSpawn(EventPlayerSpawn spawn) => OnPlayerSpawned();

        private void Ultimate()
        {
            if (WarcraftPlayer.GetAbilityLevel(3) < 1 || !IsAbilityReady(3)) return;

            if (_hasUsedDivineResurrection)
            {
                Player.PrintToChat($"{ChatColors.Red}Divine resurrection already used this round.{ChatColors.Default}");
                return;
            }

            var playerToRevive = GetRandomFallenAlly();
            if (playerToRevive == null)
            {
                Player.PrintToChat(" " + Localizer["paladin.revive.none"]);
                return;
            }

            _hasUsedDivineResurrection = true;
            StartCooldown(3);

            if (!Warcraft.RollChance((float)(DivineResurrectionChance * 100)))
            {
                Player.PrintToChat($" {Localizer["paladin.revive.fail"]}");
                return;
            }

            if (!DivineResurrection(playerToRevive))
            {
                _hasUsedDivineResurrection = false;
            }
        }

        private CCSPlayerController GetRandomFallenAlly()
        {
            var deadTeamPlayers = Utilities.GetPlayers()
                .Where(x => x.Team == Player.Team && !x.PawnIsAlive && x.IsValid)
                .ToList();

            if (!deadTeamPlayers.Any())
                return null;

            int randomIndex = RandomProvider.Next(deadTeamPlayers.Count);
            return deadTeamPlayers[randomIndex];
        }

        private bool DivineResurrection(CCSPlayerController playerToRevive)
        {
            if (playerToRevive == null || !playerToRevive.IsValid)
            {
                Player.PrintToChat(" " + Localizer["paladin.revive.none"]);
                return false;
            }

            // Check if there are any players on the same team
            //Revive
            playerToRevive.Respawn();

            Server.NextFrame(() => {
                if (playerToRevive.IsAlive() && Player.IsAlive()) {
                    var targetPawn = playerToRevive.PlayerPawn.Value;
                    var ownerPawn = Player.PlayerPawn.Value;
                    if (targetPawn != null && ownerPawn != null) {
                        targetPawn.Teleport(Player.CalculatePositionInFront(10, 60), ownerPawn.GetEyeAngles(), new Vector());
                    }
                }
            });

            playerToRevive.PrintToChat(" " + Localizer["paladin.revive"]);
            Utilities.GetPlayers().ForEach(x =>
                x.PrintToChat(" " + Localizer["paladin.revive.other", playerToRevive.GetRealPlayerName(), Player.GetRealPlayerName()]));

            return true;
        }

        private void PlayerHurtOther(EventPlayerHurtOther @event)
        {
            var attacker = @event.Attacker;
            var victim = @event.Userid;
            if (attacker == null || attacker.UserId != Player.UserId || victim == null || !victim.IsAlive()) return;

            var victimPawn = victim.PlayerPawn.Value;
            if (victimPawn == null) return;

            //Smite
            if (victimPawn.ArmorValue > 0 && Warcraft.RollAbilityCheck(WarcraftPlayer.GetAbilityLevel(2), 75))
            {
                @event.AddBonusDamage(0, WarcraftPlayer.GetAbilityLevel(2) * 5, abilityName: GetAbility(2).DisplayName);
                Warcraft.SpawnParticle(victimPawn.AbsOrigin.Clone().Add(z: 40), "particles/survival_fx/gas_cannister_impact_child_flash.vpcf", 1);
                victim.EmitSound("Weapon_Taser.Hit", volume: 0.1f);
            }
        }

        internal class HealingAuraEffect(CCSPlayerController owner, float onTickInterval) : WarcraftEffect(owner, onTickInterval: onTickInterval)
        {
            public override void OnStart() {}
            public override void OnTick()
            {
                var currentAbilityLevel = Owner.GetWarcraftPlayer().GetAbilityLevel(0);
                var auraSize = currentAbilityLevel * 100 + 100;

                var ownerPawn = Owner?.PlayerPawn?.Value;
                if (ownerPawn == null) return;

                var healingZone = Warcraft.CreateBoxAroundPoint(ownerPawn.AbsOrigin, auraSize, auraSize, auraSize);
                //healingZone.Show(duration: 2); //Debug
                //Find players within area
                var playersToHeal = Utilities.GetPlayers()
                    .Where(x => {
                        if (!x.AllyOf(Owner) || !x.PawnIsAlive || !Owner.IsValid) return false;
                        var pawn = x.PlayerPawn?.Value;
                        return pawn != null && healingZone.Contains(pawn.AbsOrigin.Clone().Add(z: 20));
                    })
                    .ToList();

                if (playersToHeal.Any())
                {
                    foreach (var player in playersToHeal)
                    {
                        var playerPawn = player.PlayerPawn.Value;
                        if (playerPawn != null && playerPawn.Health < playerPawn.MaxHealth)
                        {
                            player.Heal(currentAbilityLevel, healer: Owner);
                            Warcraft.SpawnParticle(playerPawn.AbsOrigin.Clone().Add(z: 40), "particles/ui/ammohealthcenter/ui_hud_kill_burn_fire.vpcf", 1);
                        }
                    }
                }
            }
            public override void OnFinish(){}
        }
    }
}
