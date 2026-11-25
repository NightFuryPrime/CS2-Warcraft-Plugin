using CounterStrikeSharp.API.Core;
using WarcraftPlugin.Helpers;
using WarcraftPlugin.Models;

namespace WarcraftPlugin.Core
{
    internal readonly record struct PlayerProgressSnapshot(
        long SteamId,
        string PlayerName,
        string CurrentRace,
        int CurrentXp,
        int CurrentLevel,
        int AmountToLevel,
        int Ability1Level,
        int Ability2Level,
        int Ability3Level,
        int Ability4Level)
    {
        internal static bool TryCreate(CCSPlayerController player, out PlayerProgressSnapshot snapshot)
        {
            snapshot = default;

            if (player == null || !player.IsValid || player.IsBot)
                return false;

            var wcPlayer = WarcraftPlugin.Instance.GetWcPlayer(player);
            if (wcPlayer == null || string.IsNullOrWhiteSpace(wcPlayer.className))
                return false;

            snapshot = FromPlayer(player, wcPlayer);
            return true;
        }

        internal static PlayerProgressSnapshot FromPlayer(CCSPlayerController player, WarcraftPlayer wcPlayer)
        {
            return new PlayerProgressSnapshot(
                SteamId: (long)player.SteamID,
                PlayerName: player.GetRealPlayerName(),
                CurrentRace: wcPlayer.className,
                CurrentXp: wcPlayer.currentXp,
                CurrentLevel: wcPlayer.currentLevel,
                AmountToLevel: wcPlayer.amountToLevel,
                Ability1Level: wcPlayer.GetAbilityLevel(0),
                Ability2Level: wcPlayer.GetAbilityLevel(1),
                Ability3Level: wcPlayer.GetAbilityLevel(2),
                Ability4Level: wcPlayer.GetAbilityLevel(3));
        }
    }
}
