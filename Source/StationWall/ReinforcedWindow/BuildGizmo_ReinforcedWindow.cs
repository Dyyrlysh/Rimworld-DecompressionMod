using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace StationWall.ReinforcedWindow
{
    [HarmonyPatch(typeof(Building), "GetGizmos")]
    public static class Building_GetGizmos_ReinforcedWindow_Patch
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Building __instance)
        {
            // Return all original gizmos first
            foreach (var gizmo in __result)
            {
                yield return gizmo;
            }

            // Only add our gizmo to StationWall and GravshipHull
            if (__instance.Faction == Faction.OfPlayer &&
                (__instance.def.defName == "StationWall" || __instance.def.defName == "GravshipHull"))
            {
                var windowDef = DefDatabase<ThingDef>.GetNamedSilentFail("ReinforcedWindow");
                if (windowDef != null && windowDef.BuildableByPlayer)
                {
                    yield return new Command_Action
                    {
                        defaultLabel = "Reinforced window",
                        icon = windowDef.uiIcon ?? BaseContent.BadTex,
                        action = delegate
                        {
                            // Find the build designator for reinforced windows
                            var designator = DefDatabase<DesignationCategoryDef>.AllDefs
                                .SelectMany(cat => cat.AllResolvedDesignators)
                                .OfType<Designator_Build>()
                                .FirstOrDefault(des => des.PlacingDef == windowDef);

                            if (designator != null)
                            {
                                Find.DesignatorManager.Select(designator);
                            }
                        }
                    };
                }
            }
        }
    }
}