using HarmonyLib;
using System;
using System.Reflection;
using Verse;
using Verse.AI;

namespace DecompressionMod
{
    [StaticConstructorOnStartup]
    internal static class HarmonyAudit
    {
        static HarmonyAudit()
        {
            var owner = "DecompressionMod.AirlockPatches";
            foreach (var m in HarmonyLib.Harmony.GetAllPatchedMethods())
            {
                var info = HarmonyLib.Harmony.GetPatchInfo(m);
                if (info?.Owners?.Contains(owner) == true)
                    Log.Message($"[DecompressionMod] Patched OK: {m.DeclaringType?.FullName}.{m.Name}");
            }
        }
    }

    // Patch 1: PathGrid.CalculatedCostAt
    [HarmonyPatch]
    public static class Patch_PathGrid_CalculatedCostAt
    {
        static MethodBase TargetMethod()
            => AccessTools.Method(typeof(PathGrid), "CalculatedCostAt",
                new Type[] { typeof(IntVec3), typeof(bool), typeof(IntVec3), typeof(int?) });

        static void Postfix(PathGrid __instance, IntVec3 c, bool perceivedStatic, IntVec3 prevCell, int? baseCostOverride, ref int __result)
        {
            try
            {
                var map = __instance.map;
                if (map == null) return;

                if (c.GetEdifice(map) is Building_MiniAirlock)
                    __result = 10;
            }
            catch (Exception ex) { if (Prefs.DevMode) Log.Warning($"[DecompressionMod] PathGrid.CalculatedCostAt postfix error: {ex.Message}"); }
        }
    }

    // Patch 2: PathGrid.WalkableFast(IntVec3)


    // Patch 3: PathFinder.GetBuildingCost

}
