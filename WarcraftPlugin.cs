using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using CS2MenuManager.API.Class;
using MenuApi = CS2MenuManager.API.Class.MenuManager;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using WarcraftPlugin.Adverts;
using WarcraftPlugin.Core;
using WarcraftPlugin.Core.Effects;
using WarcraftPlugin.Core.Preload;
using WarcraftPlugin.Events;
using WarcraftPlugin.Helpers;
using WarcraftPlugin.Items;
using WarcraftPlugin.lang;
using WarcraftPlugin.Menu;
using WarcraftPlugin.Menu.WarcraftMenu;
using WarcraftPlugin.Models;
using WarcraftPlugin.Diagnostics;
using WarcraftDatabase = WarcraftPlugin.Core.Database;

namespace WarcraftPlugin
{
    public class Config : BasePluginConfig
    {
        [JsonPropertyName("ConfigVersion")] public override int Version { get; set; } = 10;
        [JsonPropertyName("DeactivatedClasses")] public string[] DeactivatedClasses { get; set; } = [];
        [JsonPropertyName("ShowCommandAdverts")] public bool ShowCommandAdverts { get; set; } = true;
        [JsonPropertyName("DefaultClass")] public string DefaultClass { get; set; } = "ranger";
        [JsonPropertyName("DisableNamePrefix")] public bool DisableNamePrefix { get; set; } = false;
        [JsonPropertyName("XpPerKill")] public float XpPerKill { get; set; } = 10;
        [JsonPropertyName("XpHeadshotModifier")] public float XpHeadshotModifier { get; set; } = 0.15f;
        [JsonPropertyName("XpKnifeModifier")] public float XpKnifeModifier { get; set; } = 0.25f;
        [JsonPropertyName("XpPerRoundWin")] public int XpPerRoundWin { get; set; } = 30;
        [JsonPropertyName("XpPerAssist")] public int XpPerAssist { get; set; } = 5;
        [JsonPropertyName("XpPerBombPlant")] public int XpPerBombPlant { get; set; } = 15;
        [JsonPropertyName("XpPerBombDefuse")] public int XpPerBombDefuse { get; set; } = 20;
        [JsonPropertyName("EnableLevelDifferenceXp")] public bool EnableLevelDifferenceXp { get; set; } = true;
        [JsonPropertyName("MatchReset")] public bool MatchReset { get; set; } = false;
        [JsonPropertyName("MaxInventoryItems")] public int MaxInventoryItems { get; set; } = 2;
        [JsonPropertyName("EnableBotUltimates")] public bool EnableBotUltimates { get; set; } = true;
        [JsonPropertyName("TotalLevelRequired")]
        public Dictionary<string, int> TotalLevelRequired { get; set; } = new()
        {
            {"shadowblade", 50}, {"dwarf_engineer", 60}, {"death_weaver", 70},
            {"silent_assassin", 80}, {"hammerstorm", 90}, {"sacred_warrior", 100}
        };
        [JsonPropertyName("ItemOverrides")]
        public Dictionary<string, Dictionary<string, object>> ItemOverrides { get; set; } =
            Shop.Items.GetConfigurableProperties();

        [JsonPropertyName("EnableDebugLogs")] public bool EnableDebugLogs { get; set; } = false;

    }

    public static class WarcraftPlayerExtensions
    {
        public static WarcraftPlayer GetWarcraftPlayer(this CCSPlayerController player)
        {
            return WarcraftPlugin.Instance.GetWcPlayer(player);
        }
    }

    public class WarcraftPlugin : BasePlugin, IPluginConfig<Config>
    {
        private static WarcraftPlugin _instance;
        public static WarcraftPlugin Instance => _instance;

        public override string ModuleName => "Warcraft";
        public override string ModuleVersion => "4.0.5";

        public const int MaxLevel = 16;
        public const int MaxSkillLevel = 5;
        public const int MaxUltimateLevel = 1;

        private readonly ConcurrentDictionary<IntPtr, WarcraftPlayer> WarcraftPlayers = new();
        private EventSystem _eventSystem;
        internal XpSystem XpSystem;
        internal ClassManager classManager;
        public EffectManager EffectManager;
        internal CooldownManager CooldownManager;
        internal BotUltimateController BotUltimateController;
        internal AdvertManager AdvertManager;
        private WarcraftDatabase _database;
        private Timer _saveClientsTimer;

        public Config Config { get; set; } = null!;

        internal WarcraftPlayer GetWcPlayer(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return null;

            WarcraftPlayers.TryGetValue(player.Handle, out var wcPlayer);

            return wcPlayer;
        }

        internal void SetWcPlayer(CCSPlayerController player, WarcraftPlayer wcPlayer)
        {
            if (player == null || !player.IsValid)
                return;

            if (wcPlayer == null)
            {
                WarcraftPlayers.TryRemove(player.Handle, out _);
            }
            else
            {
                WarcraftPlayers[player.Handle] = wcPlayer;
            }
        }

        private WarcraftPlayer CreateBotPlayer(CCSPlayerController player)
        {
            var classes = classManager.GetAllClasses();
            var randomClass = classes[Random.Shared.Next(classes.Length)];

            var info = new ClassInformation
            {
                SteamId = 0,
                RaceName = randomClass.InternalName,
                CurrentXp = 0,
                CurrentLevel = 0,
                AmountToLevel = XpSystem.GetXpForLevel(0)
            };

            var wcPlayer = new WarcraftPlayer(player);
            wcPlayer.LoadClassInformation(info, XpSystem);
            XpSystem.AutoSpendSkillPoints(wcPlayer);
            return wcPlayer;
        }

        internal static void RefreshPlayerName(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            if (Instance.Config.DisableNamePrefix) return;

            var warcraftPlayer = Instance.GetWcPlayer(player);

            if (warcraftPlayer == null) return;

            var playerNameClean = player.GetRealPlayerName();
            var playerNameWithPrefix = $"{warcraftPlayer.GetLevel()} [{warcraftPlayer.GetClass().LocalizedDisplayName}] {playerNameClean}";

            player.PlayerName = playerNameWithPrefix;
            Utilities.SetStateChanged(player, "CBasePlayerController", "m_iszPlayerName");

            Instance.AddTimer(1, () =>
            {
                if (player == null || !player.IsValid) return;
                player.PlayerName = playerNameWithPrefix;
                Utilities.SetStateChanged(player, "CBasePlayerController", "m_iszPlayerName");
            });
        }

        public override void Load(bool hotReload)
        {
            base.Load(hotReload);

            if (!string.IsNullOrEmpty(Server.MapName) && !hotReload)
            {
                //Plugin loaded using 'css_plugins load', resources potentially not precached
                Server.PrintToChatAll($" {ChatColors.Green}Warcraft {ChatColors.Red}loaded after map start, {ChatColors.Orange}reload the map {ChatColors.Red}to avoid errors.");
            }

            Localizer = LocalizerMiddleware.Load(Localizer, ModuleDirectory);

            MenuApi.ReloadConfig();

            _instance ??= this;

            XpSystem = new XpSystem(this, Config, Localizer);
            XpSystem.GenerateXpCurve(110, 1.07f, MaxLevel);

            _database = new WarcraftDatabase();
            classManager = new ClassManager();
            classManager.Initialize(ModuleDirectory, Config);

            EffectManager = new EffectManager();
            EffectManager.Initialize();

            CooldownManager = new CooldownManager();
            CooldownManager.Initialize();

            if (Config.EnableBotUltimates)
            {
                BotUltimateController = new BotUltimateController();
                BotUltimateController.Initialize();
            }

            if (Config.ShowCommandAdverts)
            {
                AdvertManager = new AdvertManager();
                AdvertManager.Initialize();
            }

            List<string> ultimateAliases =
            [
                "ultimate", "ult",
                ..Localizer["command.ultimate"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ];
            foreach (var alias in ultimateAliases)
                AddUniqueCommand(alias, "ultimate", UltimatePressed);

            List<string> classAliases =
            [
                "changerace", "changeclass", "race", "class", "rpg", "cr",
                ..Localizer["command.changeclass"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ];
            foreach (var alias in classAliases)
                AddUniqueCommand(alias, "change class", (player, _) => ShowClassMenu(player));

            List<string> resetAliases =
            [
                "reset", "resetskills", "resetskillpoints",
                ..Localizer["command.reset"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ];
            foreach (var alias in resetAliases)
                AddUniqueCommand(alias, "reset skills", CommandResetSkills);

            List<string> factoryResetAliases =
            [
                "factoryreset", "fullreset", "resetlevels",
                ..Localizer["command.factoryreset"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ];
            foreach (var alias in factoryResetAliases)
                AddUniqueCommand(alias, "reset levels", CommandFactoryReset);

            List<string> addXpAliases =
                [
                "addxp", "givexp", "xpadd",
                ..Localizer["command.addxp"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ];
            foreach (var alias in addXpAliases)
                AddUniqueCommand(alias, "addxp", CommandAddXp);

            List<string> skillsAliases =
            [
                "skills", "level",
                ..Localizer["command.skills"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ];
            foreach (var alias in skillsAliases)
                AddUniqueCommand(alias, "skills", (player, _) => ShowSkillsMenu(player));

            List<string> shopAliases =
            [
                "shopmenu", "shop", "buymenu",
                ..Localizer["command.shopmenu"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ];
            foreach (var alias in shopAliases)
                AddUniqueCommand(alias, "open item shop", (player, _) => ShowShopMenu(player));

            List<string> infoAliases =
            [
                "info", "wcinfo",
                ..Localizer["command.info"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ];
            foreach (var alias in infoAliases)
                AddUniqueCommand(alias, "show warcraft info", CommandPlayerInfo);

            List<string> helpAliases =
            [
                "rpg_help", "commands", "wcs", "war3menu",
                ..Localizer["command.help"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ];
            foreach (var alias in helpAliases)
                AddUniqueCommand(alias, "list all commands", CommandHelp);

            List<string> cooldownAliases =
            [
                "cooldown.test", "testcooldowns",
                ..Localizer["command.cooldown.test"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ];
            foreach (var alias in cooldownAliases)
                AddUniqueCommand(alias, "run cooldown diagnostics", CommandCooldownDiagnostics);

            RegisterListener<Listeners.OnClientPutInServer>(OnClientPutInServerHandler);
            RegisterListener<Listeners.OnMapStart>(OnMapStartHandler);
            RegisterListener<Listeners.OnMapEnd>(OnMapEndHandler);
            RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnectHandler);

            RegisterListener<Listeners.OnServerPrecacheResources>((manifest) =>
            {
                //Models - kept here for backwards compatibility
                manifest.AddResource("models/props/de_dust/hr_dust/dust_crates/dust_crate_style_01_32x32x32.vmdl");
                manifest.AddResource("models/tools/bullet_hit_marker.vmdl");
                manifest.AddResource("models/generic/bust_02/bust_02_a.vmdl"); //destructable prop
                manifest.AddResource("models/weapons/w_muzzlefireshape.vmdl"); //fireball
                manifest.AddResource("models/anubis/structures/pillar02_base01.vmdl"); //spring trap
                manifest.AddResource("sounds/physics/body/body_medium_break3.vsnd");

                //preload class specific resources
                foreach (var resources in classManager.GetAllClasses().SelectMany(x => x.PreloadResources).ToList())
                {
                    manifest.AddResource(resources);
                }

                foreach (var resource in classManager.GetCustomHeroResources())
                {
                    manifest.AddResource(resource);
                }

                foreach (var p in Particles.Paths)
                {
                    manifest.AddResource(p);
                }

            });

            StartSaveClientsTimer();

            _eventSystem = new EventSystem(this, Config);
            _eventSystem.Initialize();

            VolumeFix.Load();

            _database.Initialize(ModuleDirectory);
        }

        private void AddUniqueCommand(string name, string description, CommandInfo.CommandCallback method)
        {
            if (!CommandDefinitions.Any(x => x.Name == name))
            {
                AddCommand(name, description, method);
            }
        }

        private void ShowSkillsMenu(CCSPlayerController player)
        {
            SkillsMenu.Show(GetWcPlayer(player));
        }

        private async void ShowClassMenu(CCSPlayerController player)
        {
            try
            {
                var databaseClassInformation = await _database.LoadClassInformationFromDatabase(player);
                ClassMenu.Show(player, databaseClassInformation);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WarcraftPlugin] Error in ShowClassMenu: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                player.PrintToChat($" {ChatColors.Red}Error loading class menu. Please try again later.");
            }
        }

        private void ShowShopMenu(CCSPlayerController player)
        {
            var wcPlayer = GetWcPlayer(player);
            if (wcPlayer != null)
                ShopMenu.Show(wcPlayer);
        }

        [RequiresPermissions("@css/root")]
        private void CommandCooldownDiagnostics(CCSPlayerController admin, CommandInfo commandInfo)
        {
            CooldownDiagnostics.Run(admin);
        }

        [RequiresPermissions("@css/addxp")]
        private void CommandAddXp(CCSPlayerController admin, CommandInfo commandInfo)
        {
            var xpArg = commandInfo.ArgByIndex(1);
            if (string.IsNullOrWhiteSpace(xpArg) || !int.TryParse(xpArg, out var xpToAdd))
            {
                admin.PrintToChat("Missing XP amount. Correct usage: !addxp <amount> [target]");
                return;
            }

            var target = admin;

            var targetArg = commandInfo.ArgByIndex(2);
            if (!string.IsNullOrWhiteSpace(targetArg))
            {
                var resolvedTarget = GetTarget(commandInfo, 2);
                if (resolvedTarget == null) return;

                target = resolvedTarget;
            }

            commandInfo.ReplyToCommand(Localizer["xp.add", xpToAdd, target.PlayerName]);
            XpSystem.AddXp(target, xpToAdd);
        }

        //[RequiresPermissions("@css/setlevel")]
        //private void CommandSetLevel(CCSPlayerController admin, CommandInfo commandInfo)
        //{
        //    var levelArg = commandInfo.ArgByIndex(1);
        //    if (string.IsNullOrWhiteSpace(levelArg) || !int.TryParse(levelArg, out var level))
        //    {
        //        admin.PrintToChat("Missing level. Correct usage: !setlevel <level> [target]");
        //        return;
        //    }

        //    var target = admin;

        //    var targetArg = commandInfo.ArgByIndex(2);
        //    if (!string.IsNullOrWhiteSpace(targetArg))
        //    {
        //        var resolvedTarget = GetTarget(commandInfo, 2);
        //        if (resolvedTarget == null) return;

        //        target = resolvedTarget;
        //    }

        //    TODO set level
        //}

        private static CCSPlayerController GetTarget(CommandInfo command, int argIndex)
        {
            var matches = command.GetArgTargetResult(argIndex).ToList();
            var arg = command.GetArg(argIndex);

            switch (matches.Count)
            {
                case 0:
                    command.ReplyToCommand($"Target \"{arg}\" not found.");
                    return null;

                case 1:
                    return matches[0];

                default:
                    command.ReplyToCommand($"Multiple targets found for \"{arg}\".");
                    return null;
            }
        }

        private void CommandHelp(CCSPlayerController player, CommandInfo commandinfo)
        {
            player.PrintToChat($" {Localizer["command.help.description"]}");
        }

        private void CommandPlayerInfo(CCSPlayerController player, CommandInfo commandInfo)
        {
            var target = player;
            var targetArg = commandInfo.ArgByIndex(1);
            if (!string.IsNullOrWhiteSpace(targetArg))
            {
                var resolved = GetTarget(commandInfo, 1);
                if (resolved == null)
                {
                    player.PrintToChat($" {Localizer["command.info.target_not_found"]}");
                    return;
                }
                target = resolved;
            }

            foreach (var line in BuildPlayerInfoLines(player, target))
            {
                player.PrintToChat(line);
            }
        }

        private IEnumerable<string> BuildPlayerInfoLines(CCSPlayerController requester, CCSPlayerController target)
        {
            var wcPlayer = GetWcPlayer(target);
            if (wcPlayer == null)
            {
                yield return $" {Localizer["command.info.unavailable"]}";
                yield break;
            }

            var warcraftClass = wcPlayer.GetClass();
            var level = wcPlayer.GetLevel();
            var xpGoal = wcPlayer.amountToLevel == 0 ? wcPlayer.currentXp : wcPlayer.amountToLevel;
            var xpRemaining = wcPlayer.amountToLevel > 0 ? Math.Max(0, wcPlayer.amountToLevel - wcPlayer.currentXp) : 0;

            yield return $" {ChatColors.Gold}{Localizer["command.info.header", target.GetRealPlayerName()]}";
            yield return $" {ChatColors.Blue}{Localizer["command.info.race"]}{ChatColors.Default}: {warcraftClass.LocalizedDisplayName}";
            yield return $" {ChatColors.Blue}{Localizer["command.info.level.title"]}{ChatColors.Default}: {Localizer["command.info.level", level, MaxLevel, wcPlayer.currentXp, xpGoal, xpRemaining]}";

            yield return $" {ChatColors.Blue}{Localizer["command.info.abilities"]}{ChatColors.Default}:";
            for (int i = 0; i < warcraftClass.Abilities.Count; i++)
            {
                var ability = warcraftClass.GetAbility(i);
                var abilityLevel = wcPlayer.GetAbilityLevel(i);
                var maxAbilityLevel = WarcraftPlayer.GetMaxAbilityLevel(i);
                yield return $"   {ability.DisplayName}: {abilityLevel}/{maxAbilityLevel}";
            }

            foreach (var buffLine in BuildBuffLines(target))
            {
                yield return buffLine;
            }
        }

        private IEnumerable<string> BuildBuffLines(CCSPlayerController target)
        {
            var pawn = target.PlayerPawn?.Value;
            var speed = pawn?.VelocityModifier ?? 1f;
            var gravity = pawn?.GravityScale ?? 1f;
            var damageReduction = 0f;
            var hasImmunity = false;

            if (pawn != null)
            {
                try
                {
                    hasImmunity = Schema.GetSchemaValue<bool>(pawn.Handle, "CCSPlayerPawn", "m_bGunGameImmunity");
                }
                catch
                {
                    hasImmunity = false;
                }
            }

            yield return $" {ChatColors.Blue}{Localizer["command.info.buffs"]}{ChatColors.Default}:";
            yield return $"   {Localizer["command.info.buff.speed", speed * 100]}";
            yield return $"   {Localizer["command.info.buff.gravity", gravity * 100]}";
            yield return $"   {Localizer["command.info.buff.damage", damageReduction * 100]}";
            yield return $"   {Localizer["command.info.buff.immunity", hasImmunity ? Localizer["command.info.value.yes"] : Localizer["command.info.value.no"]]}";
        }

        private void CommandResetSkills(CCSPlayerController client, CommandInfo commandinfo)
        {
            var wcPlayer = GetWcPlayer(client);

            var abilityCount = wcPlayer.GetClass().Abilities.Count;
            for (int i = 0; i < abilityCount; i++)
            {
                wcPlayer.SetAbilityLevel(i, 0);
            }

            if (XpSystem.GetFreeSkillPoints(wcPlayer) > 0)
            {
                SkillsMenu.Show(wcPlayer);
            }
        }

        private void CommandFactoryReset(CCSPlayerController client, CommandInfo commandInfo)
        {
            var wcPlayer = GetWcPlayer(client);
            wcPlayer.currentLevel = 0;
            wcPlayer.currentXp = 0;
            CommandResetSkills(client, commandInfo);
            client.PlayerPawn.Value.CommitSuicide(false, false);
        }

        private void OnClientDisconnectHandler(int slot)
        {
            var player = new CCSPlayerController(NativeAPI.GetEntityFromIndex(slot + 1));
            // No bots or invalid/non-existent clients.
            if (!player.IsValid || player.IsBot) return;

            // Fire and forget save
            _ = _database.SavePlayerToDatabase(player);
            SetWcPlayer(player, null);
            WeaponInsuranceService.Clear(player);
        }

        private void StartSaveClientsTimer()
        {
            _saveClientsTimer?.Kill();
            _saveClientsTimer = AddTimer(60.0f, () => _ = SaveClientsSafeAsync(), TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        }

        private async Task SaveClientsSafeAsync()
        {
            try
            {
                await _database.SaveClients();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WarcraftPlugin] Error while saving clients: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        private void OnMapEndHandler()
        {
            EffectManager.DestroyAllEffects();
            WeaponInsuranceService.Reset();
            if (Config.MatchReset)
            {
                _database.ResetClients();
            }
            else
            {
                _ = _database.SaveClients();
            }
        }

        private void OnMapStartHandler(string mapName)
        {
            if (Config.MatchReset)
            {
                _database.ResetClients();
            }
            StartSaveClientsTimer();
            WeaponInsuranceService.Reset();
        }

        private async void OnClientPutInServerHandler(int slot)
        {
            try
            {
                var player = new CCSPlayerController(NativeAPI.GetEntityFromIndex(slot + 1));
                Console.WriteLine($"Put in server {player.Handle}");
                if (!player.IsValid) return;

                WarcraftPlayer wcPlayer = player.IsBot
                    ? CreateBotPlayer(player)
                    : await _database.LoadPlayerFromDatabase(player, XpSystem);

                if (!player.IsValid || wcPlayer == null)
                    return;

                SetWcPlayer(player, wcPlayer);
                Console.WriteLine("Player just connected: " + wcPlayer);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WarcraftPlugin] Error in OnClientPutInServerHandler: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        internal async Task<WarcraftPlayer> ChangeClass(CCSPlayerController player, string classInternalName)
        {
            if (player == null || !player.IsValid)
                return null;

            var warcraftPlayer = GetWcPlayer(player);
            if (warcraftPlayer == null || string.IsNullOrWhiteSpace(classInternalName))
                return warcraftPlayer;

            var targetClass = classManager.GetAllClasses()
                .FirstOrDefault(x => x.InternalName.Equals(classInternalName, StringComparison.OrdinalIgnoreCase));

            if (targetClass == null)
            {
                player.PrintToChat($" Unable to switch class, '{classInternalName}' is not available.");
                return warcraftPlayer;
            }

            if (warcraftPlayer.className.Equals(targetClass.InternalName, StringComparison.OrdinalIgnoreCase))
            {
                warcraftPlayer.DesiredClass = null;
                return warcraftPlayer;
            }

            var preservedItems = warcraftPlayer.Items?.Select(item => item.Clone()).ToList();

            var oldClassName = warcraftPlayer.className;

            try
            {
                // 1. Save old class progress (Async, DB thread)
                await _database.SavePlayerToDatabase(player);

                // 2. Perform game state mutations on Main Thread
                var tcs = new TaskCompletionSource<bool>();
                Server.NextFrame(() =>
                {
                    try
                    {
                        if (!player.IsValid)
                        {
                            tcs.SetResult(false);
                            return;
                        }

                        EffectManager?.DestroyEffects(player, EffectDestroyFlags.OnChangingRace);
                        warcraftPlayer.GetClass()?.ResetCooldowns();
                        warcraftPlayer.GetClass()?.PlayerChangingToAnotherRace();
                        tcs.SetResult(true);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                });
                await tcs.Task;

                warcraftPlayer.DesiredClass = null;

                // 4. Save new class to DB (Async, DB thread)
                await _database.SaveCurrentClass(player, targetClass.InternalName);

                // 5. Load new class stats (Async, DB thread)
                var refreshedPlayer = await _database.LoadPlayerFromDatabase(player, XpSystem);

                if (refreshedPlayer == null)
                {
                    Console.WriteLine($"[Warcraft] Failed to reload player after class change. Reverting.");
                    await _database.SaveCurrentClass(player, oldClassName);

                    Server.NextFrame(() => player.PrintToChat($" {ChatColors.Red}Error changing class. Please try again."));
                    return warcraftPlayer;
                }

                var applyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Server.NextFrame(() =>
                {
                    try
                    {
                        if (!player.IsValid)
                        {
                            applyTcs.SetResult(false);
                            return;
                        }

                        refreshedPlayer.DesiredClass = null;
                        SetWcPlayer(player, refreshedPlayer);

                        if (preservedItems?.Count > 0)
                        {
                            foreach (var storedItem in preservedItems)
                            {
                                if (refreshedPlayer.AddItem(storedItem) && player.PawnIsAlive)
                                {
                                    storedItem.Apply(player);
                                }
                            }
                        }

                        AbilityBenefitAnnouncer.SendRoundSummary(player, refreshedPlayer);
                        applyTcs.SetResult(true);
                    }
                    catch (Exception ex)
                    {
                        applyTcs.SetException(ex);
                    }
                });
                await applyTcs.Task;
                return refreshedPlayer;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WarcraftPlugin] Error in ChangeClass: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Server.NextFrame(() => player.PrintToChat($" {ChatColors.Red}An error occurred while changing class."));

                warcraftPlayer.DesiredClass = null;
                try
                {
                    await _database.SaveCurrentClass(player, oldClassName);
                }
                catch (Exception revertEx)
                {
                    Console.WriteLine($"[WarcraftPlugin] Failed to revert class change: {revertEx.Message}");
                    Console.WriteLine(revertEx.StackTrace);
                }
                return warcraftPlayer;
            }
        }

        internal void SavePlayerProgress(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            _ = _database?.SavePlayerToDatabase(player);
        }

        internal void DebugLog(string message)
        {
            if (Config?.EnableDebugLogs != true) return;
            var line = $"[Warcraft][DEBUG] {message}";
            Server.PrintToConsole(line);
            Console.WriteLine(line);
        }

        internal void DebugSetModel(string context, CBaseModelEntity entity, string model)
        {
            if (Config?.EnableDebugLogs != true) return;
            var handle = entity?.EntityHandle.Raw ?? 0;
            var owner = (entity as CCSPlayerPawn)?.Controller?.Value?.PlayerName ?? string.Empty;
            DebugLog($"{context}: SetModel '{model}' (handle {handle}, owner '{owner}')");
        }

        private void UltimatePressed(CCSPlayerController client, CommandInfo commandinfo)
        {
            // CRITICAL FIX: Prevent dead players from using ultimate abilities
            if (!client.IsAlive())
            {
                client.PrintToCenter(" You must be alive to use your ultimate!");
                client.PlayLocalSound("sounds/common/talk.vsnd");
                return;
            }

            var warcraftPlayer = client.GetWarcraftPlayer();
            if (warcraftPlayer.GetAbilityLevel(3) < 1)
            {
                client.PrintToCenter(" " + Localizer["no.ultimate"]);
                client.PlayLocalSound("sounds/common/talk.vsnd");
            }
            else if (!warcraftPlayer.GetClass().IsAbilityReady(3))
            {
                client.PrintToCenter(" " + Localizer["ultimate.countdown", Math.Ceiling(warcraftPlayer.GetClass().AbilityCooldownRemaining(3))]);
                client.PlayLocalSound("sounds/common/talk.vsnd");
            }
            else
            {
                GetWcPlayer(client)?.GetClass()?.InvokeAbility(3);
            }
        }

        public void OnConfigParsed(Config config)
        {
            Config = config;
            Shop.Items.ApplyOverrides(config);
        }

        public override void Unload(bool hotReload)
        {
            foreach (var player in Utilities.GetPlayers())
            {
                //Avoid getting stuck in old menu
                player.EnableMovement();
            }
            _eventSystem?.Dispose();
            BotUltimateController?.Dispose();
            AdvertManager?.Cancel();
            _saveClientsTimer?.Kill();
            _database?.SaveClients().GetAwaiter().GetResult();
            _database?.Dispose();
            VolumeFix.Unload();
            base.Unload(hotReload);
        }
    }
}
