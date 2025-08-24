using HarmonyLib;
using RimWorld;
using System;
using Verse;

namespace DecompressionMod
{
    // Simple patch to ensure our airlock properly reports its ExchangeVacuum state
    [HarmonyPatch(typeof(Building), "ExchangeVacuum", MethodType.Getter)]
    public static class Patch_Building_ExchangeVacuum_MiniAirlock
    {
        public static bool Prefix(Building __instance, ref bool __result)
        {
            // Only handle our airlock
            if (__instance is Building_MiniAirlock airlock)
            {
                // Use our custom logic - only exchange when both doors are open
                __result = airlock.InnerDoorOpen && airlock.OuterDoorOpen;
                return false; // Skip original method
            }

            return true; // Continue with original method for other buildings
        }
    }
}