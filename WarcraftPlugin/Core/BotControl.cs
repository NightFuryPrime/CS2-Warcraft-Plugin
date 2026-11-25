using CounterStrikeSharp.API.Core;
using System;
using WarcraftPlugin.Helpers;

namespace WarcraftPlugin.Core;

internal readonly record struct UltimateControllerResolution(
    bool Success,
    CCSPlayerController Controller,
    string FailureReason)
{
    internal static UltimateControllerResolution Succeeded(CCSPlayerController controller) => new(true, controller, null);

    internal static UltimateControllerResolution Failed(string reason) => new(false, null, reason);
}

internal static class BotControl
{
    internal static UltimateControllerResolution ResolveUltimateController(CCSPlayerController client)
    {
        if (client?.IsValid != true)
            return UltimateControllerResolution.Failed("invalid-client");

        if (!client.ControllingBot)
            return UltimateControllerResolution.Succeeded(client);

        var botController = client.OriginalControllerOfCurrentPawn?.Value;
        if (botController?.IsValid == true && botController.IsBot)
            return UltimateControllerResolution.Succeeded(botController);

        return UltimateControllerResolution.Failed("missing-controlled-bot-controller");
    }

    internal static bool IsControllerOfBot(CCSPlayerController candidate, CCSPlayerController bot)
    {
        if (candidate?.IsValid != true || bot?.IsValid != true || !candidate.ControllingBot)
            return false;

        var controlledBot = candidate.OriginalControllerOfCurrentPawn?.Value;
        return controlledBot?.IsValid == true && controlledBot.Handle == bot.Handle;
    }

    internal static bool IsValidReviveTarget(CCSPlayerController caster, CCSPlayerController candidate)
    {
        return IsValidReviveTargetState(
            caster?.IsValid == true,
            caster?.Handle ?? IntPtr.Zero,
            (int)(caster?.Team ?? 0),
            candidate?.IsValid == true,
            candidate?.Handle ?? IntPtr.Zero,
            (int)(candidate?.Team ?? 0),
            candidate.IsAlive(),
            IsControllerOfBot(candidate, caster));
    }

    internal static bool IsValidReviveTargetState(
        bool casterValid,
        IntPtr casterHandle,
        int casterTeam,
        bool candidateValid,
        IntPtr candidateHandle,
        int candidateTeam,
        bool candidateAlive,
        bool candidateControlsCaster)
    {
        if (!casterValid || !candidateValid)
            return false;

        if (candidateHandle == casterHandle)
            return false;

        if (candidateTeam != casterTeam)
            return false;

        if (candidateControlsCaster)
            return false;

        return !candidateAlive;
    }
}
