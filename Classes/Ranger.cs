using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using g3;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using WarcraftPlugin.Core;
using WarcraftPlugin.Core.Effects;
using WarcraftPlugin.Events.ExtendedEvents;
using WarcraftPlugin.Helpers;
using WarcraftPlugin.Models;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace WarcraftPlugin.Classes
{
    internal class Ranger : WarcraftClass
    {
        private const string TrapIndicatorParticle = "particles/ui/ui_gameplay_ring.vpcf";
        public override string DisplayName => "Ranger";
        public override Color DefaultColor => Color.Green;

        public override List<string> PreloadResources => [TrapIndicatorParticle];

        private List<IWarcraftAbility> _abilities;

        public override List<IWarcraftAbility> Abilities => _abilities ??=
        [
            new WarcraftCooldownAbility("Light footed", "Nimbly perform a dash in midair, cooldown decreases from 18s to 6s as you level up.", () => 18f / Math.Max(1, WarcraftPlayer.GetAbilityLevel(0))),
            new WarcraftAbility("Ensnare trap", "Place a trap by throwing a decoy that deals 10/20/30/40/50 damage on trigger."),
            new WarcraftAbility("Marksman", "Deal 2/4/6/8/10 bonus damage with scoped weapons."),
            new WarcraftCooldownAbility("Arrowstorm", "Ping a point to rain arrows for 10s, hurting and slowing foes", 50f)
        ];

        public override void Register()
        {
            RegisterHooks()
                .Hook<EventPlayerJump>(PlayerJump)
                .Hook<EventDecoyStarted>(DecoyStart, HookMode.Post)
                .Hook<EventPlayerHurtOther>(PlayerHurtOther)
                .Hook<EventPlayerPing>(PlayerPing)
                .Hook<EventPlayerSpawn>(OnPlayerSpawn);

            RegisterAbilities()
                .Bind(3, Ultimate);
        }

        public override void OnPlayerSpawned()
        {
            // Start dash passive listener
            new DashPassiveEffect(Player).Start();

            if (WarcraftPlayer.GetAbilityLevel(1) > 0)
            {
                var decoy = Player.GiveWeapon<CDecoyGrenade>("weapon_decoy");
                if (decoy == null) return;
                decoy.AttributeManager.Item.CustomName = Localizer["ranger.ability.1"];
            }
        }

        private void OnPlayerSpawn(EventPlayerSpawn @event) => OnPlayerSpawned();

        private void PlayerHurtOther(EventPlayerHurtOther @event)
        {
            var attacker = @event.Attacker;
            var victim = @event.Userid;
            if (attacker == null || attacker.UserId != Player.UserId || victim == null || !victim.IsAlive()) return;

            var markmansLevel = WarcraftPlayer.GetAbilityLevel(2);

            if (markmansLevel > 0 && WeaponTypes.IsSniper(@event.Weapon))
            {
                @event.AddBonusDamage(markmansLevel * 2, abilityName: GetAbility(2).DisplayName);
                Warcraft.SpawnParticle(victim.PlayerPawn.Value.AbsOrigin.Clone().Add(z: 60), "particles/maps/de_overpass/chicken_impact_burst2.vpcf");
            }
        }

        #region Dash
        private bool _canDashAirborne = false;
        private void PlayerJump(EventPlayerJump @event)
        {
            // First jump off the ground enables the dash-in-air logic
            _canDashAirborne = true;
        }

        internal class DashPassiveEffect(CCSPlayerController owner) : WarcraftEffect(owner, 999999, onTickInterval: 0.1f)
        {
            private ulong _previousButtonState = 0;
            private float _gracePeriod = 0.2f;
            private float _spawnTime;

            public override void OnStart()
            {
                _spawnTime = Server.CurrentTime;
            }

            public override void OnTick()
            {
                if (!Owner.IsAlive()) return;
                var pawn = Owner.PlayerPawn?.Value;
                var movement = pawn?.MovementServices as CCSPlayer_MovementServices;
                var ranger = Owner.GetWarcraftPlayer().GetClass() as Ranger;

                if (pawn == null || movement == null || ranger == null) return;

                ulong currentButtonState = movement.Buttons.ButtonStates[0];
                bool jumpPressed = (_previousButtonState & (ulong)PlayerButtons.Jump) == 0 &&
                                  (currentButtonState & (ulong)PlayerButtons.Jump) != 0;

                // Toggle dash ready state
                if ((pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) != 0)
                {
                    ranger._canDashAirborne = false;
                }

                if (ranger._canDashAirborne && jumpPressed && Server.CurrentTime > _spawnTime + _gracePeriod)
                {
                    if (ranger.WarcraftPlayer.GetAbilityLevel(0) > 0 && ranger.IsAbilityReady(0))
                    {
                        ranger.Dash(pawn, movement, currentButtonState);
                        ranger._canDashAirborne = false; // Reset until next ground jump
                        ranger.StartCooldown(0);
                    }
                }

                _previousButtonState = currentButtonState;
            }

            public override void OnFinish()
            {
                // No specific cleanup needed for passive dash listener
            }
        }

        private void Dash(CCSPlayerPawn pawn, CCSPlayer_MovementServices movement, ulong buttonState)
        {
            var directionAngle = pawn.GetEyeAngles();
            directionAngle.Y +=
                        (buttonState & (ulong)PlayerButtons.Back) != 0 ? 180 :
                        (buttonState & (ulong)PlayerButtons.Moveleft) != 0 ? 90 :
                        (buttonState & (ulong)PlayerButtons.Moveright) != 0 ? -90 : 0;

            var directionVec = new Vector();
            NativeAPI.AngleVectors(directionAngle.Handle, directionVec.Handle, nint.Zero, nint.Zero);

            if (directionVec.Z < 0.275) directionVec.Z = 0.275f;

            directionVec *= 800; // Increased force slightly

            pawn.AbsVelocity.X = directionVec.X;
            pawn.AbsVelocity.Y = directionVec.Y;
            pawn.AbsVelocity.Z = directionVec.Z;
            Player.EmitSound("Default.WalkJump", volume: 0.5f);
            Player.PrintToChat(" [Ranger] Dashing midair!");
        }

        #endregion
        #region Trap
        private void DecoyStart(EventDecoyStarted decoy)
        {
            if (WarcraftPlayer.GetAbilityLevel(1) > 0)
            {
                var projectile = Utilities.GetEntityFromIndex<CDecoyProjectile>(decoy.Entityid);
                if (projectile == null)
                {
                    WarcraftPlugin.Instance.DebugLog("[Ranger] DecoyStart projectile missing, trap aborted.");
                    return;
                }

                new EnsnaringTrapEffect(Player, 20, projectile, new Vector(decoy.X, decoy.Y, decoy.Z)).Start();
            }
        }
        #endregion

        private void Ultimate()
        {
            if (WarcraftPlayer.GetAbilityLevel(3) < 1 || !IsAbilityReady(3)) return;

            // Hack to get players aim point in the world, see player ping event
            Player.ExecuteClientCommandFromServer("player_ping");
        }

        private void PlayerPing(EventPlayerPing ping)
        {
            if (WarcraftPlayer.GetAbilityLevel(3) < 1 || !IsAbilityReady(3)) return;
            StartCooldown(3);
            new ArrowStormEffect(Player, 10, new Vector(ping.X, ping.Y, ping.Z)).Start();
        }

        internal class DashEffect(CCSPlayerController owner) : WarcraftEffect(owner, onTickInterval: 0.1f)
        {
            private readonly float _extraJumpDelay = 0.2f;
            private float _extraJumpDelayTick;
            private float _groundCheckDelayTick;
            private byte? _originalCollisionGroup;
            private ulong _previousButtonState = 0;

            public override void OnStart()
            {
                _extraJumpDelayTick = Server.CurrentTime + _extraJumpDelay;
                _groundCheckDelayTick = Server.CurrentTime + 0.1f; // Don't check ground for first 0.1 seconds
                EnableGhostMovement(); // Enable ghost movement so dash works properly
                WarcraftPlugin.Instance?.DebugLog($"[Ranger] Dash effect started, can dash at {_extraJumpDelayTick}.");
            }

            public override void OnTick()
            {
                //Effect is destroyed if player is on the ground
                if (!Owner.IsAlive())
                {
                    this.Destroy();
                    return;
                }

                var pawn = Owner.PlayerPawn?.Value;
                if (pawn == null) return;

                // Only check ground status after grace period to avoid destroying effect during jump startup
                if (Server.CurrentTime > _groundCheckDelayTick && (pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) != 0)
                {
                    WarcraftPlugin.Instance?.DebugLog("[Ranger] Dash effect destroyed because the player landed.");
                    this.Destroy();
                    return;
                }

                var movement = pawn.MovementServices as CCSPlayer_MovementServices;
                if (movement == null) return;

                ulong currentButtonState = movement.Buttons.ButtonStates[0];

                // Detect jump button PRESS (not hold) - check if button wasn't pressed before and is now pressed
                bool jumpPressed = (_previousButtonState & (ulong)PlayerButtons.Jump) == 0 &&
                                  (currentButtonState & (ulong)PlayerButtons.Jump) != 0;

                if (jumpPressed)
                {
                    WarcraftPlugin.Instance?.DebugLog($"[Ranger] Jump press detected at {Server.CurrentTime}, delayOver={Server.CurrentTime > _extraJumpDelayTick}.");
                }

                if (Server.CurrentTime > _extraJumpDelayTick && jumpPressed)
                {
                    WarcraftPlugin.Instance?.DebugLog("[Ranger] Triggering midair dash.");
                    Dash(pawn, movement, currentButtonState);
                    Owner.GetWarcraftPlayer().GetClass().StartCooldown(0);
                    this.Destroy();
                }

                _previousButtonState = currentButtonState;
            }

            private void Dash(CCSPlayerPawn pawn, CCSPlayer_MovementServices movement, ulong buttonState)
            {
                var directionAngle = pawn.GetEyeAngles();

                directionAngle.Y +=
                            (buttonState & (ulong)PlayerButtons.Back) != 0 ? 180 :
                            (buttonState & (ulong)PlayerButtons.Moveleft) != 0 ? 90 :
                            (buttonState & (ulong)PlayerButtons.Moveright) != 0 ? -90 : 0;

                var directionVec = new Vector();
                NativeAPI.AngleVectors(directionAngle.Handle, directionVec.Handle, nint.Zero, nint.Zero);

                // Always shoot us up a little bit if were on the ground and not aiming up.
                if (directionVec.Z < 0.275)
                {
                    directionVec.Z = 0.275f;
                }

                directionVec *= 700;

                pawn.AbsVelocity.X = directionVec.X;
                pawn.AbsVelocity.Y = directionVec.Y;
                pawn.AbsVelocity.Z = directionVec.Z;

                Owner.EmitSound("Default.WalkJump", volume: 0.5f);
            }

            public override void OnFinish()
            {
                RestoreCollisionGroup();
            }

            private void EnableGhostMovement()
            {
                var collision = Owner.PlayerPawn.Value?.Collision;
                if (collision == null) return;

                _originalCollisionGroup ??= collision.CollisionGroup;
                collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_PASSABLE_DOOR;
            }

            private void RestoreCollisionGroup()
            {
                if (_originalCollisionGroup == null) return;

                var collision = Owner.PlayerPawn.Value?.Collision;
                if (collision != null)
                {
                    collision.CollisionGroup = _originalCollisionGroup.Value;
                }
                _originalCollisionGroup = null;
            }
        }

        internal class EnsnaringTrapEffect(CCSPlayerController owner, float duration, CDecoyProjectile decoyProjectile, Vector trapPosition) : WarcraftEffect(owner, duration, destroyOnDeath: false)
        {
            private readonly Vector _trapPosition = trapPosition.Clone();
            private readonly CDecoyProjectile _decoyProjectile = decoyProjectile;
            private CParticleSystem _trapIndicator;
            private Box3d _triggerZone;
            private readonly List<CCSPlayerController> _snaredPlayers = [];

            private bool IsTriggered { get; set; } = false;

            public override void OnStart()
            {
                if (_decoyProjectile != null && _decoyProjectile.IsValid)
                {
                    _decoyProjectile.MoveType = MoveType_t.MOVETYPE_NONE;
                    _decoyProjectile.Teleport(_trapPosition, _decoyProjectile.AbsRotation, new Vector());
                    var collision = _decoyProjectile.Collision;
                    if (collision != null)
                    {
                        collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_PASSABLE_DOOR;
                        collision.SolidType = SolidType_t.SOLID_NONE;
                    }

                    Schema.SetSchemaValue(_decoyProjectile.Handle, "CBaseEntity", "m_nNextThinkTick", -1);
                    Schema.SetSchemaValue(_decoyProjectile.Handle, "CDecoyProjectile", "m_shotsRemaining", 0);
                    Schema.SetSchemaValue(_decoyProjectile.Handle, "CDecoyProjectile", "m_fExpireTime", Server.CurrentTime + Duration + 1);
                }

                _trapIndicator = Warcraft.SpawnParticle(_trapPosition.Clone().Add(z: 8), TrapIndicatorParticle, Duration);
                _triggerZone = Warcraft.CreateBoxAroundPoint(_trapPosition, 100, 100, 100);
                //_triggerZone.Show(duration: Duration); //Debug
            }

            public override void OnTick()
            {
                if (!Owner.IsValid) return;

                if (!IsTriggered)
                {
                    //Find players in trap trigger zone
                    var players = PlayerCache.GetPlayers();
                    var playersInHurtZone = players.Where(x =>
                    {
                        if (!x.PawnIsAlive || x.AllyOf(Owner)) return false;
                        var pawn = x.PlayerPawn?.Value;
                        return pawn != null && _triggerZone.Contains(pawn.AbsOrigin.Clone().Add(z: 20));
                    }).ToList();

                    if (playersInHurtZone.Count != 0)
                    {
                        TriggerTrap(playersInHurtZone);
                        IsTriggered = true;
                    }
                }
            }

            private void TriggerTrap(List<CCSPlayerController> playersInTrap)
            {
                Warcraft.SpawnParticle(_trapPosition.Clone().Add(z: 20), "particles/explosions_fx/explosion_hegrenade_water_ripple.vpcf", 1);
                _decoyProjectile?.RemoveIfValid();
                _trapIndicator?.RemoveIfValid();
                _trapIndicator = null;

                foreach (var player in playersInTrap)
                {
                    var pawn = player.PlayerPawn?.Value;
                    var movement = pawn?.MovementServices;
                    if (pawn == null || movement == null) continue;
                    _snaredPlayers.Add(player);

                    player.TakeDamage(Owner.GetWarcraftPlayer().GetAbilityLevel(1) * 10, Owner, KillFeedIcon.tripwirefire);

                    // Launch player upward
                    pawn.AbsVelocity.Add(z: 600);

                    // Apply slow
                    SetVelocityMultiplier(player, 0f, $"trap-{player.Handle}");
                    SetMaxSpeedMultiplier(player, 20f / Math.Max(movement.Maxspeed, 1f), $"trap-{player.Handle}");
                    RefreshPlayerState(player);
                    Warcraft.SpawnParticle(player.CalculatePositionInFront(10, 60), "particles/blood_impact/blood_impact_basic.vpcf");
                    player.PrintToChat($" {Localizer["ranger.trappedby", Owner.GetRealPlayerName()]}");
                    Owner.PrintToChat($" {Localizer["ranger.trapowner", player.GetRealPlayerName()]}");
                }

                //Clean-up
                WarcraftPlugin.Instance.AddTimer(3f, () =>
                {
                    foreach (var player in playersInTrap)
                    {
                        RestorePlayerMovement(player);
                    }
                    this.Destroy();
                });
            }

            public override void OnFinish()
            {
                _decoyProjectile?.RemoveIfValid();
                _trapIndicator?.RemoveIfValid();
                _trapIndicator = null;
                RestoreAllSnaredPlayers();
            }

            private void RestoreAllSnaredPlayers()
            {
                foreach (var snared in _snaredPlayers.ToArray())
                {
                    RestorePlayerMovement(snared);
                }
                _snaredPlayers.Clear();
            }

            private void RestorePlayerMovement(CCSPlayerController player)
            {
                if (player == null || !player.IsValid) return;
                RemoveStateContributions(player, $"trap-{player.Handle}");
                RefreshPlayerState(player);
                _snaredPlayers.RemoveAll(x => x?.Handle == player.Handle);
            }
        }

        internal class ArrowStormEffect(CCSPlayerController owner, float duration, Vector stormpos) : WarcraftEffect(owner, duration)
        {
            private readonly int _stormHeight = 150;
            private readonly int _stormArea = 280;
            private readonly int _arrowsPerVolley = 4;
            private Box3d _arrowSpawnBox;
            private Box3d _hurtBox;

            public override void OnStart()
            {
                var spawnBoxPoint = stormpos.With(z: stormpos.Z + _stormHeight);
                _arrowSpawnBox = Warcraft.CreateBoxAroundPoint(spawnBoxPoint, _stormArea, _stormArea, 50);

                var hurtBoxPoint = stormpos.With(z: stormpos.Z + _stormHeight / 2);
                _hurtBox = Warcraft.CreateBoxAroundPoint(hurtBoxPoint, _stormArea, _stormArea, _stormHeight);
                //_hurtBox.Show(duration: Duration); //Debug
                Owner.EmitSound("UI.DeathMatch.Dominating", volume: 0.5f);
            }

            public override void OnTick()
            {
                Warcraft.SpawnParticle(_arrowSpawnBox.Center.ToVector().Add(z: 20), "particles/explosions_fx/explosion_hegrenade_water_ripple.vpcf", 1);

                HurtPlayersInside();

                for (int i = 0; i < _arrowsPerVolley; i++)
                {
                    SpawnArrow();
                }
            }

            private void HurtPlayersInside()
            {
                //Find players within area
                var players = PlayerCache.GetPlayers();
                var playersInHurtZone = players.Where(x =>
                {
                    if (!x.IsAlive() || x.AllyOf(Owner)) return false;
                    var pawn = x.PlayerPawn?.Value;
                    return pawn != null && _hurtBox.Contains(pawn.AbsOrigin.Clone().Add(z: 20));
                }).ToList();

                //Set movement speed + small hurt
                foreach (var player in playersInHurtZone)
                {
                    var pawn = player.PlayerPawn?.Value;
                    if (pawn == null) continue;

                    player.TakeDamage(4, Owner, KillFeedIcon.flair0);
                    new ArrowStormSlowEffect(Owner, player, 0.35f).Start();
                    Warcraft.SpawnParticle(player.CalculatePositionInFront(10, 60), "particles/blood_impact/blood_impact_basic.vpcf");
                }
            }

            private void SpawnArrow()
            {
                //Calculate new arrow pos
                var arrowSpawn = _arrowSpawnBox.GetRandomPoint();
                //Spawn arrow
                var arrow = Warcraft.CreateEntityByNameSafe<CHEGrenadeProjectile>("hegrenade_projectile");
                if (arrow == null) return;
                arrow.Teleport(arrowSpawn, new QAngle(z: -90), new Vector());
                arrow.DispatchSpawn();
                WarcraftPlugin.Instance.DebugSetModel("[Ranger] ArrowStorm", arrow, "models/tools/bullet_hit_marker.vmdl");
                arrow.SetModel("models/tools/bullet_hit_marker.vmdl");
                arrow.SetColor(Color.FromArgb(255, 45, 25, 25));
                arrow.SetScale(0.5f);

                arrow.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_NEVER;
                arrow.Collision.SolidFlags = 12;
                arrow.Collision.SolidType = SolidType_t.SOLID_VPHYSICS;

                if (Owner.PlayerPawn != null)
                {
                    Schema.SetSchemaValue(arrow.Handle, "CBaseGrenade", "m_hThrower", Owner.PlayerPawn.Raw); //Fixes killfeed
                }

                //Cleanup
                WarcraftPlugin.Instance.AddTimer(0.6f, () => { arrow?.RemoveIfValid(); });
            }

            public override void OnFinish() { }
        }

        internal sealed class ArrowStormSlowEffect(CCSPlayerController owner, CCSPlayerController victim, float duration)
            : WarcraftEffect(owner, duration, destroyOnDeath: false, onTickInterval: 1f)
        {
            private readonly CCSPlayerController _victim = victim;

            public override void OnStart()
            {
                if (!_victim.TryGetAlivePawn(out _)) return;
                SetVelocityMultiplier(_victim, 0f, "arrowstorm");
                RefreshPlayerState(_victim);
            }

            public override void OnTick() { }

            public override void OnFinish()
            {
                RemoveStateContributions(_victim, "arrowstorm");
                RefreshPlayerState(_victim);
            }
        }
    }
}
