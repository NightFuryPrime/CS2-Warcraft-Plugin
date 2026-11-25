using System;
using System.Collections.Generic;
using System.Linq;
using CounterStrikeSharp.API.Core;
using WarcraftPlugin.Core;
using WarcraftPlugin.Models;

namespace WarcraftPlugin.Diagnostics
{
    internal static class CooldownDiagnostics
    {
        internal static void Run(CCSPlayerController invoker)
        {
            if (invoker == null || !invoker.IsValid)
                return;

            var plugin = WarcraftPlugin.Instance;
            var results = new List<string>();

            var stubReset = CreateStubPlayer(invoker);
            for (int i = 0; i < stubReset.AbilityCooldowns.Count; i++)
            {
                stubReset.AbilityCooldowns[i] = 5 + i;
            }
            CooldownManager.ResetCooldowns(stubReset);
            var resetSuccess = stubReset.AbilityCooldowns.All(v => Math.Abs(v) < 0.001f);
            results.Add($"Reset clears cooldown list: {(resetSuccess ? "PASS" : "FAIL")}");

            var stubTimers = CreateStubPlayer(invoker);
            float[] durations = [0.35f, 0.6f, 0.85f];
            for (int i = 0; i < durations.Length; i++)
            {
                CooldownManager.StartCooldown(stubTimers, i, durations[i]);
            }

            invoker.PrintToChat(" [CooldownTest] Running asynchronous cooldown test...");
            plugin.AddTimer(1.2f, () =>
            {
                var timersExpired = durations
                    .Select((_, index) => stubTimers.AbilityCooldowns[index] <= 0.01f)
                    .All(x => x);

                results.Add($"Multi-timer expiration: {(timersExpired ? "PASS" : "FAIL")}");
                CooldownManager.ResetCooldowns(stubTimers);

                foreach (var line in results)
                {
                    invoker.PrintToChat($" [CooldownTest] {line}");
                }
            });
        }

        private static WarcraftPlayer CreateStubPlayer(CCSPlayerController controller)
        {
            return new WarcraftPlayer(controller);
        }
    }
}
