using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace DecompressionMod
{
    [HarmonyPatch(typeof(Section), "RegenerateAllLayers")]
    public static class Section_RegenerateAllLayers_Patch
    {
        private static readonly FieldInfo layersField = AccessTools.Field(typeof(Section), "layers");

        [HarmonyPostfix]
        public static void Postfix(Section __instance)
        {
            try
            {
                // Only add our layers if Odyssey is active
                if (!ModsConfig.OdysseyActive) return;

                var layers = (List<SectionLayer>)layersField?.GetValue(__instance);
                if (layers == null) return;

                // Check if our layers already exist
                bool hasStationWallLayer = false;
                bool hasStationFloorLayer = false;

                foreach (var layer in layers)
                {
                    if (layer is SectionLayer_StationWall)
                        hasStationWallLayer = true;
                    if (layer is SectionLayer_StationFloor)
                        hasStationFloorLayer = true;
                }

                // Add our layers if they don't exist
                if (!hasStationWallLayer)
                {
                    var stationWallLayer = new SectionLayer_StationWall(__instance);
                    layers.Add(stationWallLayer);
                    //Log.Warning("[StationWall] Added SectionLayer_StationWall to section!");
                }

                if (!hasStationFloorLayer)
                {
                    var stationFloorLayer = new SectionLayer_StationFloor(__instance);
                    layers.Add(stationFloorLayer);
                    //Log.Warning("[StationFloor] Added SectionLayer_StationFloor to section!");
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[StationMod] Error adding SectionLayers: {ex}");
            }
        }
    }

    // Alternative approach - patch the Section constructor
    [HarmonyPatch(typeof(Section), MethodType.Constructor, new[] { typeof(IntVec3), typeof(Map) })]
    public static class Section_Constructor_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Section __instance)
        {
            try
            {
                // Only add our layers if Odyssey is active
                if (!ModsConfig.OdysseyActive) return;

                var layersField = AccessTools.Field(typeof(Section), "layers");
                var layers = (List<SectionLayer>)layersField?.GetValue(__instance);
                if (layers == null) return;

                // Add our custom SectionLayers
                var stationWallLayer = new SectionLayer_StationWall(__instance);
                layers.Add(stationWallLayer);

                var stationFloorLayer = new SectionLayer_StationFloor(__instance);
                layers.Add(stationFloorLayer);

                //Log.Warning("[StationMod] Added both SectionLayers to new section!");
            }
            catch (System.Exception ex)
            {
                Log.Error($"[StationMod] Error in Section constructor patch: {ex}");
            }
        }
    }
}