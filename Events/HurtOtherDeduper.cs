using System;
using System.Collections.Generic;
using WarcraftPlugin.Helpers;

namespace WarcraftPlugin.Events;

internal sealed class HurtOtherDeduper
{
    private readonly Dictionary<HurtOtherDedupeKey, float> _lastShotgunHitByKey = [];

    internal bool ShouldProcess(nint attackerHandle, nint victimHandle, string weaponName, float currentTime)
    {
        if (!WeaponTypes.IsShotgun(weaponName))
            return true;

        var key = new HurtOtherDedupeKey(attackerHandle, victimHandle, WeaponTypes.NormalizeName(weaponName));
        if (_lastShotgunHitByKey.TryGetValue(key, out var lastTime) && lastTime == currentTime)
            return false;

        _lastShotgunHitByKey[key] = currentTime;
        Prune(currentTime);
        return true;
    }

    private void Prune(float currentTime)
    {
        if (_lastShotgunHitByKey.Count < 128)
            return;

        var staleKeys = new List<HurtOtherDedupeKey>();
        foreach (var entry in _lastShotgunHitByKey)
        {
            if (currentTime - entry.Value > 1f)
            {
                staleKeys.Add(entry.Key);
            }
        }

        foreach (var key in staleKeys)
        {
            _lastShotgunHitByKey.Remove(key);
        }
    }

    private readonly record struct HurtOtherDedupeKey(nint Attacker, nint Victim, string Weapon);
}
