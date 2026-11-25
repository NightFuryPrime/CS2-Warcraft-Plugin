using System;

namespace WarcraftPlugin.Helpers;

internal readonly record struct DamageReductionResult(
    int ReducedHealth,
    int ReducedArmor,
    int PreventedHealth,
    int PreventedArmor);

internal static class DamageMath
{
    internal static DamageReductionResult CalculateReduction(int incomingHealth, int incomingArmor, double reduction)
    {
        var clampedReduction = Math.Clamp(reduction, 0, 0.9);
        var reducedHealth = Reduce(incomingHealth, clampedReduction);
        var reducedArmor = Reduce(incomingArmor, clampedReduction);

        return new DamageReductionResult(
            reducedHealth,
            reducedArmor,
            Math.Max(0, incomingHealth - reducedHealth),
            Math.Max(0, incomingArmor - reducedArmor));
    }

    internal static bool IsProjectedLethal(int currentHealth, int incomingHealthDamage)
    {
        return currentHealth <= 0 || incomingHealthDamage >= Math.Max(1, currentHealth);
    }

    private static int Reduce(int incoming, double reduction)
    {
        if (incoming <= 0)
            return 0;

        return Math.Max(0, (int)Math.Round(incoming * (1 - reduction)));
    }
}
