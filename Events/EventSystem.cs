using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using WarcraftPlugin.Core;
using WarcraftPlugin.Core.Effects;
using WarcraftPlugin.Diagnostics;
using WarcraftPlugin.Events.ExtendedEvents;
using WarcraftPlugin.Helpers;
using WarcraftPlugin.Menu.WarcraftMenu;
using WarcraftPlugin.Models;
using static CounterStrikeSharp.API.Core.BasePlugin;

namespace WarcraftPlugin.Events
{
    internal class EventSystem
    {
        private readonly WarcraftPlugin _plugin;
        private readonly Config _config;
        private readonly List<GameAction> _gameActions = [];
        private Timer? _playerSpottedTimer;
        private bool _weaponAcquireHooked;
        private bool _disposed;

        internal EventSystem(WarcraftPlugin plugin, Config config)
        {
            _plugin = plugin;
            _config = config;
        }

        internal void Initialize()
        {
            // middleware
            RegisterEventHandler<EventPlayerSpawn>(PlayerSpawnHandler, HookMode.Pre);
            RegisterEventHandler<EventPlayerHurt>(PlayerHurtHandler, HookMode.Pre);
            RegisterEventHandler<EventPlayerDeath>(PlayerDeathHandler, HookMode.Pre);
            RegisterEventHandler<EventRoundEnd>(RoundEnd, HookMode.Pre);
            RegisterEventHandler<EventRoundStart>(RoundStart, HookMode.Pre);
            RegisterEventHandler<EventPlayerDisconnect>(PlayerDisconnectHandler, HookMode.Pre);
            RegisterEventHandler<EventBombPlanted>(BombPlantedHandler, HookMode.Post);
            RegisterEventHandler<EventBombDefused>(BombDefusedHandler, HookMode.Post);

            //Virtual functions
            VirtualFunctions.CCSPlayer_ItemServices_CanAcquireFunc.Hook(OnWeaponCanAcquire, HookMode.Pre);
            _weaponAcquireHooked = true;

            //Custom events
            _playerSpottedTimer = _plugin.AddTimer(1, PlayerSpottedOnRadar, TimerFlags.REPEAT);

            //Register event handlers dynamically from classes
            RegisterDynamicEventHandlers();
        }

        private void RegisterEventHandler<T>(GameEventHandler<T> handler, HookMode hookMode = HookMode.Post) where T : GameEvent
        {
            if (CanAddGameAction(typeof(T), hookMode))
                _plugin.RegisterEventHandler(handler, hookMode);
        }

        private bool CanAddGameAction(Type gameEventType, HookMode hookMode)
        {
            if (typeof(ICustomGameEvent).IsAssignableFrom(gameEventType)) return false;
            if (!_gameActions.Any(x => x.EventType == gameEventType && x.HookMode == hookMode))
            {
                _gameActions.Add(new GameAction { EventType = gameEventType, HookMode = hookMode });
                return true;
            }

            //Event+Hookmode already registered
            return false;
        }

        private void RegisterDynamicEventHandlers()
        {
            foreach (var warcraftClass in _plugin.classManager.GetAllClasses())
            {
                foreach (var gameAction in warcraftClass.GetEventListeners())
                {
                    if (!CanAddGameAction(gameAction.EventType, gameAction.HookMode)) continue;

                    var handlerMethod = typeof(EventSystem).GetMethod(
                        gameAction.HookMode == HookMode.Pre ? nameof(HandleDynamicPreEvent) : nameof(HandleDynamicPostEvent),
                        BindingFlags.Static | BindingFlags.NonPublic
                    ).MakeGenericMethod(gameAction.EventType);
                    var handlerDelegate = Delegate.CreateDelegate(typeof(GameEventHandler<>).MakeGenericType(gameAction.EventType), handlerMethod);
                    var registerMethod = typeof(WarcraftPlugin).GetMethod(nameof(WarcraftPlugin.RegisterEventHandler))
                                                            .MakeGenericMethod(gameAction.EventType);
                    registerMethod.Invoke(_plugin, [handlerDelegate, gameAction.HookMode]);
                }
            }
        }

        private static HookResult HandleDynamicPreEvent<T>(T @event, GameEventInfo info) where T : GameEvent
        {
            return HandleDynamicEvent(@event, info, HookMode.Pre);
        }

        private static HookResult HandleDynamicPostEvent<T>(T @event, GameEventInfo info) where T : GameEvent
        {
            return HandleDynamicEvent(@event, info, HookMode.Post);
        }

        private static HookResult HandleDynamicEvent<T>(T @event, GameEventInfo info, HookMode hookMode) where T : GameEvent
        {
            var userid = @event.GetType().GetProperty("Userid")?.GetValue(@event) as CCSPlayerController;
            if (userid != null)
            {
                // Invoke player specific events directly on the affected player
                userid.GetWarcraftPlayer()?.GetClass()?.InvokeEvent(@event, hookMode);
            }
            else
            {
                // Else Invoke global events on all players
                Utilities.GetPlayers().ForEach(p => { p.GetWarcraftPlayer()?.GetClass()?.InvokeEvent(@event, hookMode); });
            }
            return HookResult.Continue;
        }

        private HookResult OnWeaponCanAcquire(DynamicHook hook)
        {
            var services = hook.GetParam<CCSPlayer_ItemServices>(0);
            if (services == null || services.Pawn?.Value == null || services.Pawn.Value.Controller?.Value == null)
                return HookResult.Continue;

            CCSPlayerController client = services.Pawn.Value.Controller.Value.As<CCSPlayerController>();

            if (client == null || !client.IsValid || !client.PawnIsAlive)
                return HookResult.Continue;

            var warcraftClass = client?.GetWarcraftPlayer()?.GetClass();
            if (warcraftClass == null)
                return HookResult.Continue;

            CCSWeaponBaseVData vdata = VirtualFunctions.GetCSWeaponDataFromKeyFunc.Invoke(-1, hook.GetParam<CEconItemView>(1).ItemDefinitionIndex.ToString());

            // Weapon is restricted
            if (warcraftClass.WeaponWhitelist.Count > 0 && !warcraftClass.WeaponWhitelist.Any(whitelistName => vdata.Name.Contains(whitelistName, StringComparison.OrdinalIgnoreCase)))
            {
                hook.SetReturn(AcquireResult.InvalidItem);
                return HookResult.Stop;
            }

            return HookResult.Continue;
        }

        private void PlayerSpottedOnRadar()
        {
            var players = Utilities.GetPlayers();
            var playerDictionary = players.ToDictionary(player => player.Index);

            foreach (var spottedPlayer in players)
            {
                if (!spottedPlayer.IsAlive()) continue;

                var pawn = spottedPlayer.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid) continue;

                var spottedByMask = pawn.EntitySpottedState.SpottedByMask;

                for (int i = 0; i < spottedByMask.Length; i++)
                {
                    uint mask = spottedByMask[i];
                    int baseId = i * 32;

                    while (mask != 0)
                    {
                        int playerIndex = baseId + BitOperations.TrailingZeroCount(mask) + 1; // Offset by 1 to match the 1-based index

                        if (playerDictionary.TryGetValue((uint)playerIndex, out var spottedByPlayer) && spottedByPlayer.IsAlive())
                        {
                            var spottedPlayerClass = spottedPlayer.GetWarcraftPlayer()?.GetClass();
                            var spottedByPlayerClass = spottedByPlayer.GetWarcraftPlayer()?.GetClass();

                            var eventSpottedByEnemy = new EventSpottedByEnemy() { UserId = spottedByPlayer };
                            var eventSpottedEnemy = new EventSpottedEnemy() { UserId = spottedPlayer };

                            spottedPlayerClass?.InvokeEvent(eventSpottedByEnemy, HookMode.Pre);
                            spottedByPlayerClass?.InvokeEvent(eventSpottedEnemy, HookMode.Pre);
                            spottedPlayerClass?.InvokeEvent(eventSpottedByEnemy);
                            spottedByPlayerClass?.InvokeEvent(eventSpottedEnemy);
                        }

                        mask &= mask - 1;
                    }
                }
            }
        }

        private HookResult RoundEnd(EventRoundEnd @event, GameEventInfo info)
        {
            Utilities.GetPlayers().ForEach(p =>
            {
                WarcraftPlugin.Instance.EffectManager.DestroyEffects(p, EffectDestroyFlags.OnRoundEnd);
                p.GetWarcraftPlayer()?.GetClass()?.InvokeEvent(@event, HookMode.Pre);
            });

            var winnerProp = @event.GetType().GetProperty("Winner");
            if (winnerProp != null)
            {
                var value = winnerProp.GetValue(@event);
                CsTeam teamWinner;
                if (value is CsTeam enumTeam)
                    teamWinner = enumTeam;
                else
                    teamWinner = (CsTeam)Convert.ToInt32(value);

                if (teamWinner is CsTeam.Terrorist or CsTeam.CounterTerrorist)
                {
                    foreach (var player in Utilities.GetPlayers().Where(p => p.Team == teamWinner && !p.ControllingBot))
                    {
                        _plugin.XpSystem.AddXp(player, (int)_config.XpPerRoundWin);
                        player.PrintToChat(_plugin.Localizer["xp.roundwin", _config.XpPerRoundWin]);
                    }
                }

                foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && !p.ControllingBot))
                {
                    WarcraftPlugin.Instance.SavePlayerProgress(player);
                }
            }
            return HookResult.Continue;
        }

        private HookResult RoundStart(EventRoundStart @event, GameEventInfo info)
        {
            Utilities.GetPlayers().Where(x => !x.IsBot && !x.ControllingBot).ToList().ForEach(player =>
            {
                var warcraftPlayer = player.GetWarcraftPlayer();
                var warcraftClass = warcraftPlayer?.GetClass();

                if (warcraftClass != null)
                {
                    warcraftClass?.InvokeEvent(@event, HookMode.Pre);

                if (AbilityProgression.GetFreeSkillPoints(warcraftPlayer) > 0)
                {
                    SkillsMenu.Show(warcraftPlayer);
                }
                    else
                    {
                        var message = $"{warcraftClass.LocalizedDisplayName} ({warcraftPlayer.currentLevel})\n" +
                        (warcraftPlayer.IsMaxLevel ? "" : $"{_plugin.Localizer["xp.current"]}: {warcraftPlayer.currentXp}/{warcraftPlayer.amountToLevel}\n");

                        player.PrintToCenter(message);
                    }

                    Server.NextFrame(() =>
                    {
                        warcraftClass.ResetCooldowns();
                    });

                    warcraftPlayer.PrintItemsOwned();

                    if (!warcraftPlayer.IsMaxLevel)
                    {
                        player.PrintToChat($" {_plugin.Localizer["xp.roundinfo",
                            warcraftPlayer?.GetClass()?.LocalizedDisplayName,
                            warcraftPlayer.currentLevel,
                            warcraftPlayer.currentXp,
                            warcraftPlayer.amountToLevel]}");
                    }

                    bool classChangePending =
                        !string.IsNullOrEmpty(warcraftPlayer.DesiredClass) &&
                        !warcraftPlayer.DesiredClass.Equals(warcraftClass.InternalName, StringComparison.OrdinalIgnoreCase);

                    if (!classChangePending)
                    {
                        AbilityBenefitAnnouncer.SendRoundSummary(player, warcraftPlayer);
                    }
                }
            });
            return HookResult.Continue;
        }

        private HookResult PlayerHurtHandler(EventPlayerHurt @event, GameEventInfo _)
        {
            var victim = @event.Userid;
            var attacker = @event.Attacker;

            if (victim != null && (!victim.IsAlive())) return HookResult.Continue;

            var attackingClass = attacker?.GetWarcraftPlayer()?.GetClass();

            if (attackingClass != null)
            {
                if (attackingClass.GetKillFeedTick() != Server.CurrentTime)
                    attackingClass.ResetKillFeedIcon();

                //Prevent shotguns, etc from triggering multiple hurt other events
                if (attackingClass?.LastHurtOther != Server.CurrentTime)
                {
                    attackingClass.LastHurtOther = Server.CurrentTime;
                    var hurtOtherEvent = new EventPlayerHurtOther(@event.Handle);
                    attackingClass.InvokeEvent(hurtOtherEvent, HookMode.Pre);

                    ItemManager.OnPlayerHurtOther(hurtOtherEvent);
                    attackingClass.InvokeEvent(hurtOtherEvent);
                }
            }

            victim?.GetWarcraftPlayer()?.GetClass()?.InvokeEvent(@event, HookMode.Pre);
            ItemManager.OnPlayerHurt(@event);

            return HookResult.Continue;
        }

        private HookResult PlayerSpawnHandler(EventPlayerSpawn @event, GameEventInfo info)
        {
            var player = @event.Userid;

            var warcraftPlayer = player?.GetWarcraftPlayer();

            if (warcraftPlayer != null)
            {
                var warcraftClass = warcraftPlayer.GetClass();
                if (warcraftPlayer.DesiredClass != null && warcraftPlayer.DesiredClass != warcraftClass?.InternalName)
                {
                    WarcraftPlugin.Instance.EffectManager.DestroyEffects(player, EffectDestroyFlags.OnChangingRace);
                    _ = HandleSpawnClassChangeAsync(player, warcraftPlayer.DesiredClass);
                    return HookResult.Continue;
                }

                if (player.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist)
                    warcraftClass?.InvokeEvent(@event, HookMode.Pre);

                Server.NextFrame(() => WarcraftPlugin.RefreshPlayerName(player));

                RunWhenPawnReady(player, () =>
                {
                    var refreshedPlayer = player.GetWarcraftPlayer();
                    var refreshedClass = refreshedPlayer?.GetClass();

                    refreshedClass?.SetDefaultAppearance();
                    refreshedPlayer?.ApplyItems();
                    WeaponInsuranceService.HandlePlayerSpawn(player);
                });
            }

            return HookResult.Continue;
        }

        private async Task HandleSpawnClassChangeAsync(CCSPlayerController player, string desiredClass)
        {
            if (player == null || string.IsNullOrWhiteSpace(desiredClass))
                return;

            try
            {
                await WarcraftPlugin.Instance.ChangeClass(player, desiredClass);

                Server.NextFrame(() =>
                {
                    if (player == null || !player.IsValid)
                        return;

                    var refreshedPlayer = player.GetWarcraftPlayer();
                    var refreshedClass = refreshedPlayer?.GetClass();

                    if (player.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist)
                        refreshedClass?.OnPlayerSpawned();

                    WarcraftPlugin.RefreshPlayerName(player);

                    RunWhenPawnReady(player, () =>
                    {
                        var readyPlayer = player.GetWarcraftPlayer();
                        var readyClass = readyPlayer?.GetClass();

                        readyClass?.SetDefaultAppearance();
                        readyPlayer?.ApplyItems();
                        WeaponInsuranceService.HandlePlayerSpawn(player);
                    });
                });
            }
            catch (Exception ex)
            {
                PersistentLogger.Error(nameof(HandleSpawnClassChangeAsync), $"Spawn-time class change failed for '{player?.PlayerName}' -> '{desiredClass}'.", ex);
                Console.WriteLine($"[WarcraftPlugin] Error while handling spawn class change: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        private HookResult PlayerDeathHandler(EventPlayerDeath @event, GameEventInfo info)
        {
            var attacker = @event.Attacker;
            var victim = @event.Userid;
            var headshot = @event.Headshot;
            var assister = @event.Assister;

            if (attacker == null || victim == null) return HookResult.Continue;

            if (attacker.IsValid && victim.IsValid && attacker != victim && attacker.PlayerPawn.IsValid && attacker.PawnIsAlive && !attacker.ControllingBot)
            {
                var killerClass = attacker.GetWarcraftPlayer()?.GetClass();
                var killEvent = new EventPlayerKilledOther(@event.Handle);
                killerClass?.InvokeEvent(killEvent, HookMode.Pre);
                killerClass?.InvokeEvent(killEvent);
                var weaponName = @event.Weapon;

                if (!attacker.AllyOf(victim))
                    _plugin.XpSystem.CalculateAndAddKillXp(
                        attacker,
                        victim,
                        weaponName,
                        headshot
                    );
            }

            if (_config.XpPerAssist > 0 && assister != null && assister.IsValid && assister != attacker && assister != victim && !assister.AllyOf(victim) && assister.AllyOf(attacker))
            {
                _plugin.XpSystem.AddXpWithMessage(assister, _config.XpPerAssist, "xp.assist", victim.PlayerName);
            }

            if (victim.IsValid && attacker.IsValid)
            {
                var attackerClass = attacker.GetWarcraftPlayer()?.GetClass();
                var victimClass = victim.GetWarcraftPlayer()?.GetClass();
                WarcraftPlugin.Instance.EffectManager.DestroyEffects(victim, EffectDestroyFlags.OnDeath);
                victimClass?.InvokeEvent(@event, HookMode.Pre);
                @event.Weapon = attackerClass?.GetKillFeedIcon()?.ToString() ?? @event.Weapon;
            }

            WeaponInsuranceService.HandlePlayerDeath(victim);

            var victimWarcraft = victim.GetWarcraftPlayer();
            if (victimWarcraft?.DesiredClass != null && victimWarcraft.DesiredClass != victimWarcraft.className)
            {
                WarcraftPlugin.Instance.EffectManager.DestroyEffects(victim, EffectDestroyFlags.OnChangingRace);
                _ = WarcraftPlugin.Instance.ChangeClass(victim, victimWarcraft.DesiredClass);
                // We don't need to wait for this on death usually.
            }

            return HookResult.Continue;
        }

        private HookResult PlayerDisconnectHandler(EventPlayerDisconnect @event, GameEventInfo info)
        {
            var player = @event.Userid;
            if (player != null && player.IsValid)
            {
                var mockDeathEvent = new EventPlayerDeath(0) { Userid = @event.Userid };
                var warcraftPlayer = player?.GetWarcraftPlayer()?.GetClass();
                WarcraftPlugin.Instance.EffectManager.DestroyEffects(player, EffectDestroyFlags.OnDisconnect);
                warcraftPlayer?.InvokeEvent(mockDeathEvent, HookMode.Pre);
            }
            return HookResult.Continue;
        }

        private HookResult BombPlantedHandler(EventBombPlanted @event, GameEventInfo _)
        {
            if (_config.XpPerBombPlant <= 0) return HookResult.Continue;
            var planter = @event.Userid;

            if (planter != null && planter.IsValid && planter.Team == CsTeam.Terrorist)
            {
                _plugin.XpSystem.AddXpWithMessage(planter, _config.XpPerBombPlant, "xp.bomb.plant");
            }

            return HookResult.Continue;
        }

        private HookResult BombDefusedHandler(EventBombDefused @event, GameEventInfo _)
        {
            if (_config.XpPerBombDefuse <= 0) return HookResult.Continue;
            var defuser = @event.Userid;

            if (defuser != null && defuser.IsValid && defuser.Team == CsTeam.CounterTerrorist)
            {
                _plugin.XpSystem.AddXpWithMessage(defuser, _config.XpPerBombDefuse, "xp.bomb.defuse");
            }

            return HookResult.Continue;
        }

        private void RunWhenPawnReady(CCSPlayerController player, Action action, int attempt = 0)
        {
            const int maxAttempts = 20;

            if (player == null || !player.IsValid || action == null)
            {
                return;
            }

            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid || pawn.CBodyComponent == null)
            {
                if (attempt >= maxAttempts)
                {
                    return;
                }

                Server.NextFrame(() => RunWhenPawnReady(player, action, attempt + 1));
                return;
            }

            action();
        }

        internal void Dispose()
        {
            if (_disposed)
                return;

            _playerSpottedTimer?.Kill();
            _playerSpottedTimer = null;

            if (_weaponAcquireHooked)
            {
                VirtualFunctions.CCSPlayer_ItemServices_CanAcquireFunc.Unhook(OnWeaponCanAcquire, HookMode.Pre);
                _weaponAcquireHooked = false;
            }

            _disposed = true;
        }
    }
}
