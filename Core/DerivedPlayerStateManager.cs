using System;
using System.Collections.Generic;
using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using WarcraftPlugin.Helpers;

namespace WarcraftPlugin.Core;

internal static class DerivedPlayerStateManager
{
    private sealed class PlayerState
    {
        public bool BaselineCaptured { get; set; }
        public float BaseVelocityModifier { get; set; } = 1f;
        public float BaseGravityScale { get; set; } = 1f;
        public float BaseMaxSpeed { get; set; } = 250f;
        public int BaseMaxHealth { get; set; } = 100;

        public Dictionary<string, float> VelocityMultipliers { get; } = [];
        public Dictionary<string, float> VelocityAdditives { get; } = [];
        public Dictionary<string, float> GravityMultipliers { get; } = [];
        public Dictionary<string, float> GravityAdditives { get; } = [];
        public Dictionary<string, float> MaxSpeedMultipliers { get; } = [];
        public Dictionary<string, int> MaxHealthBonuses { get; } = [];

        public bool HasAnyContribution =>
            VelocityMultipliers.Count > 0 ||
            VelocityAdditives.Count > 0 ||
            GravityMultipliers.Count > 0 ||
            GravityAdditives.Count > 0 ||
            MaxSpeedMultipliers.Count > 0 ||
            MaxHealthBonuses.Count > 0;
    }

    private static readonly Dictionary<IntPtr, PlayerState> States = [];

    internal static void ResetPlayer(CCSPlayerController player)
    {
        if (player == null)
            return;

        States.Remove(player.Handle);
    }

    internal static void ClearAll() => States.Clear();

    internal static void SetVelocityMultiplier(CCSPlayerController player, string key, float multiplier)
    {
        SetContribution(player, key, multiplier, static state => state.VelocityMultipliers, static (state, pawn) =>
        {
            if (state.VelocityMultipliers.Count == 0 && state.VelocityAdditives.Count == 0)
            {
                state.BaseVelocityModifier = pawn.VelocityModifier;
            }
        });
    }

    internal static void SetVelocityAdditive(CCSPlayerController player, string key, float additive)
    {
        SetContribution(player, key, additive, static state => state.VelocityAdditives, static (state, pawn) =>
        {
            if (state.VelocityMultipliers.Count == 0 && state.VelocityAdditives.Count == 0)
            {
                state.BaseVelocityModifier = pawn.VelocityModifier;
            }
        });
    }

    internal static void SetGravityMultiplier(CCSPlayerController player, string key, float multiplier)
    {
        SetContribution(player, key, multiplier, static state => state.GravityMultipliers, static (state, pawn) =>
        {
            if (state.GravityMultipliers.Count == 0 && state.GravityAdditives.Count == 0)
            {
                state.BaseGravityScale = pawn.GravityScale;
            }
        });
    }

    internal static void SetGravityAdditive(CCSPlayerController player, string key, float additive)
    {
        SetContribution(player, key, additive, static state => state.GravityAdditives, static (state, pawn) =>
        {
            if (state.GravityMultipliers.Count == 0 && state.GravityAdditives.Count == 0)
            {
                state.BaseGravityScale = pawn.GravityScale;
            }
        });
    }

    internal static void SetMaxSpeedMultiplier(CCSPlayerController player, string key, float multiplier)
    {
        SetContribution(player, key, multiplier, static state => state.MaxSpeedMultipliers, static (state, pawn) =>
        {
            if (state.MaxSpeedMultipliers.Count == 0 && pawn.MovementServices != null)
            {
                state.BaseMaxSpeed = pawn.MovementServices.Maxspeed;
            }
        });
    }

    internal static void SetMaxHealthBonus(CCSPlayerController player, string key, int bonus)
    {
        SetContribution(player, key, bonus, static state => state.MaxHealthBonuses, static (state, pawn) =>
        {
            if (state.MaxHealthBonuses.Count == 0)
            {
                state.BaseMaxHealth = pawn.MaxHealth;
            }
        });
    }

    internal static void RemoveContribution(CCSPlayerController player, string key)
    {
        if (player == null || string.IsNullOrWhiteSpace(key) || !States.TryGetValue(player.Handle, out var state))
            return;

        state.VelocityMultipliers.Remove(key);
        state.VelocityAdditives.Remove(key);
        state.GravityMultipliers.Remove(key);
        state.GravityAdditives.Remove(key);
        state.MaxSpeedMultipliers.Remove(key);
        state.MaxHealthBonuses.Remove(key);
    }

    internal static void RefreshDerivedPlayerState(CCSPlayerController player, bool resetBaseline = false)
    {
        if (!player.TryGetAlivePawn(out var pawn))
            return;

        var state = GetOrCreateState(player);
        CaptureBaseline(state, pawn, resetBaseline);
        ApplyState(player, pawn, state);
    }

    private static void SetContribution<TValue>(
        CCSPlayerController player,
        string key,
        TValue value,
        Func<PlayerState, Dictionary<string, TValue>> selector,
        Action<PlayerState, CCSPlayerPawn> captureBaseline)
    {
        if (player == null || string.IsNullOrWhiteSpace(key))
            return;

        var state = GetOrCreateState(player);
        if (player.TryGetAlivePawn(out var pawn))
        {
            captureBaseline?.Invoke(state, pawn);
            state.BaselineCaptured = true;
        }
        selector(state)[key] = value;
    }

    private static PlayerState GetOrCreateState(CCSPlayerController player)
    {
        if (!States.TryGetValue(player.Handle, out var state))
        {
            state = new PlayerState();
            States[player.Handle] = state;
        }

        return state;
    }

    private static void CaptureBaseline(PlayerState state, CCSPlayerPawn pawn, bool force)
    {
        if (force || !state.BaselineCaptured)
        {
            state.BaseVelocityModifier = pawn.VelocityModifier;
            state.BaseGravityScale = pawn.GravityScale;
            state.BaseMaxSpeed = pawn.MovementServices?.Maxspeed ?? state.BaseMaxSpeed;
            state.BaseMaxHealth = pawn.MaxHealth;
            state.BaselineCaptured = true;
            return;
        }

        if (state.VelocityMultipliers.Count == 0 && state.VelocityAdditives.Count == 0)
        {
            state.BaseVelocityModifier = pawn.VelocityModifier;
        }

        if (state.GravityMultipliers.Count == 0 && state.GravityAdditives.Count == 0)
        {
            state.BaseGravityScale = pawn.GravityScale;
        }

        if (state.MaxSpeedMultipliers.Count == 0 && pawn.MovementServices != null)
        {
            state.BaseMaxSpeed = pawn.MovementServices.Maxspeed;
        }

        if (state.MaxHealthBonuses.Count == 0)
        {
            state.BaseMaxHealth = pawn.MaxHealth;
        }
    }

    private static void ApplyState(CCSPlayerController player, CCSPlayerPawn pawn, PlayerState state)
    {
        var velocity = Math.Max(0f, (state.BaseVelocityModifier + Sum(state.VelocityAdditives)) * Product(state.VelocityMultipliers));
        var gravity = Math.Max(0.05f, (state.BaseGravityScale + Sum(state.GravityAdditives)) * Product(state.GravityMultipliers));
        var maxSpeed = Math.Max(0f, state.BaseMaxSpeed * Product(state.MaxSpeedMultipliers));
        var maxHealth = Math.Max(1, state.BaseMaxHealth + Sum(state.MaxHealthBonuses));

        pawn.VelocityModifier = velocity;
        pawn.GravityScale = gravity;

        if (pawn.MovementServices != null)
        {
            pawn.MovementServices.Maxspeed = maxSpeed;
        }

        if (pawn.MaxHealth != maxHealth)
        {
            pawn.MaxHealth = maxHealth;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iMaxHealth");

            if (pawn.Health > maxHealth)
            {
                player.SetHp(maxHealth);
            }
        }
    }

    private static float Sum(Dictionary<string, float> values) => values.Values.Sum();

    private static int Sum(Dictionary<string, int> values) => values.Values.Sum();

    private static float Product(Dictionary<string, float> values)
    {
        var product = 1f;
        foreach (var value in values.Values)
        {
            product *= value;
        }

        return product;
    }
}
