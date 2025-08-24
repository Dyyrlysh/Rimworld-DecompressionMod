using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace DecompressionMod
{
    public class Decompression_Component : MapComponent
    {
        private Dictionary<Room, float> lastTickVacuumLevels = new Dictionary<Room, float>();
        private Dictionary<int, float> lastTickVacuumLevelsByRoomID = new Dictionary<int, float>();
        private List<DecompressionEvent> activeDecompressions = new List<DecompressionEvent>();
        private HashSet<Room> processedRoomsThisTick = new HashSet<Room>();

        // Simplified: Only Minor and Major severities
        private const float MinorDecompressionThreshold = 0.4f;
        private const float MajorDecompressionThreshold = 0.6f;

        public Decompression_Component(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            if (map.IsHashIntervalTick(30))
            {
                ProcessActiveDecompressions();
            }

            if (map.IsHashIntervalTick(250))
            {
                processedRoomsThisTick.Clear();
            }
        }

        public void DetectDecompressionEvents(Dictionary<Room, float> currentVacuumLevels)
        {
            foreach (var kvp in currentVacuumLevels)
            {
                Room room = kvp.Key;
                float currentVacuum = kvp.Value;

                if (room.IsDoorway || room.Cells.Count() < 3 || processedRoomsThisTick.Contains(room)) continue;

                if (lastTickVacuumLevels.TryGetValue(room, out float previousVacuum))
                {
                    float vacuumDelta = currentVacuum - previousVacuum;

                    if (vacuumDelta >= MinorDecompressionThreshold &&
                        previousVacuum < 0.25f &&
                        currentVacuum > 0.4f)
                    {
                        CreateDecompressionEvent(room, vacuumDelta);
                        processedRoomsThisTick.Add(room);
                    }
                }

                lastTickVacuumLevels[room] = currentVacuum;
            }
        }

        private void CreateDecompressionEvent(Room sourceRoom, float vacuumDelta)
        {
            int roomSize = sourceRoom.Cells.Count();
            float adjustedSeverity = vacuumDelta;

            // Check larger rooms first!
            if (roomSize > 200)        // Very large rooms
                adjustedSeverity *= 3f;
            else if (roomSize > 100)   // Large rooms  
                adjustedSeverity *= 1.5f;

            DecompressionSeverity severity = (adjustedSeverity >= 0.5f) ? DecompressionSeverity.Major : DecompressionSeverity.Minor;
            IntVec3 breachPoint = Decompression_Utility.FindSimpleBreachPoint(sourceRoom, map);

            var decompressionEvent = new DecompressionEvent
            {
                sourceRoom = sourceRoom,
                pressureDelta = vacuumDelta,
                severity = severity,
                ticksRemaining = GetDecompressionDuration(severity),
                affectedCells = sourceRoom.Cells.ToList(),
                breachPoint = breachPoint
            };

            activeDecompressions.Add(decompressionEvent);

            Log.Message($"[Decompression] Created {severity} event in room with {sourceRoom.Cells.Count()} cells");

            Decompression_Effects.ApplyDecompressionEffects(decompressionEvent, map);
            Decompression_Alerts.SendDecompressionAlert(decompressionEvent, map);
        }

        private DecompressionSeverity GetDecompressionSeverity(float delta)
        {
            if (delta >= MajorDecompressionThreshold) return DecompressionSeverity.Major;
            return DecompressionSeverity.Minor;
        }

        private int GetDecompressionDuration(DecompressionSeverity severity)
        {
            switch (severity)
            {
                case DecompressionSeverity.Major: return 90;
                case DecompressionSeverity.Minor: return 120;
                default: return 60;
            }
        }

        private void ProcessActiveDecompressions()
        {
            for (int i = activeDecompressions.Count - 1; i >= 0; i--)
            {
                var decomp = activeDecompressions[i];
                decomp.ticksRemaining--;

                if (decomp.ticksRemaining <= 0)
                {
                    activeDecompressions.RemoveAt(i);
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();

            /* if (Scribe.mode == LoadSaveMode.Saving)
            {
                // Convert room references to IDs.
                lastTickVacuumLevelsByRoomID = lastTickVacuumLevels.ToDictionary(
                    kvp => kvp.Key.ID,
                    kvp => kvp.Value) ?? new Dictionary<int, float>();
            }

            Scribe_Collections.Look(ref lastTickVacuumLevelsByRoomID, "lastTickVacuumLevelsByRoomID", LookMode.Value, LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Convert IDs back into Rooms.
                lastTickVacuumLevels = new Dictionary<Room, float>();
            } */
        }
    }
}

/* namespace StationWall
{
    public static class AtmosphereUtility
    {
        private static readonly Dictionary<Map, Dictionary<IntVec3, float>> breachIntensityByMap = new Dictionary<Map, Dictionary<IntVec3, float>>();
        private static readonly Dictionary<Map, HashSet<IntVec3>> breachCellsByMap = new Dictionary<Map, HashSet<IntVec3>>();

        public static float GetBreachIntensity(IntVec3 cell, Map map)
        {
            return breachIntensityByMap.TryGetValue(map, out var dict) && dict.TryGetValue(cell, out var intensity)
                ? intensity
                : 0f;
        }

        public static void MarkBreach(IntVec3 cell, Map map, float intensity)
        {
            if (!breachCellsByMap.TryGetValue(map, out var set))
            {
                set = new HashSet<IntVec3>();
                breachCellsByMap[map] = set;
            }
            set.Add(cell);

            if (!breachIntensityByMap.TryGetValue(map, out var dict))
            {
                dict = new Dictionary<IntVec3, float>();
                breachIntensityByMap[map] = dict;
            }
            dict[cell] = intensity;
        }

        public static bool IsBreachCell(IntVec3 cell, Map map)
        {
            return breachCellsByMap.TryGetValue(map, out var breachCells) && breachCells.Contains(cell);
        }

        public static void ClearBreaches(Map map)
        {
            breachCellsByMap.Remove(map);
            breachIntensityByMap.Remove(map);
        }
    }

    public static class VacuumHelper
    {
        public static VacuumComponent GetVacuumComponent(Map map)
        {
            return map.components.OfType<VacuumComponent>().FirstOrDefault();
        }
    }

    public class VacuumBreachUtility
    {
        public static void LogBreach(IntVec3 cell, Map map, float vacuumLevel)
        {
            if (!AtmosphereUtility.IsBreachCell(cell, map))
            {
                AtmosphereUtility.MarkBreach(cell, map, vacuumLevel);
                Log.Warning($"[Decompression] Breach detected at {cell} with vacuum level {vacuumLevel}");
            }
        }

        private static readonly HashSet<Room> flushedRooms = new HashSet<Room>();

        public static void OnRoomFlushed(Room room, float vacuumLevel, float delta)
        {
            if (flushedRooms.Contains(room))
                return;

            flushedRooms.Add(room);
            Log.Warning($"[Decompression] Room {room.ID} flushed: Δ={delta}, Vacuum={vacuumLevel}");

            foreach (var cell in room.Cells)
            {
                if (AtmosphereUtility.IsBreachCell(cell, room.Map))
                {
                    float intensity = AtmosphereUtility.GetBreachIntensity(cell, room.Map);
                    QueueDecompressionEffect(cell, room.Map, intensity);
                    // TODO: Add sparks, sound, pawn damage, etc.
                }
            }
        }

        private static readonly Queue<(IntVec3 cell, Map map, float intensity)> effectQueue = new Queue<(IntVec3 cell, Map map, float intensity)>();

        public static void QueueDecompressionEffect(IntVec3 cell, Map map, float intensity)
        {
            effectQueue.Enqueue((cell, map, intensity));
        }

        public static void ProcessEffectQueue()
        {
            int maxPerTick = 10; // tweakable
            for (int i = 0; i < maxPerTick && effectQueue.Count > 0; i++)
            {
                var (cell, map, intensity) = effectQueue.Dequeue();
                SpawnDecompressionEffect(cell, map, intensity);
            }
        }

        public static void SpawnDecompressionEffect(IntVec3 cell, Map map, float intensity)
        {
            Vector3 loc = cell.ToVector3Shifted();
            ThingDef moteDef = ThingDef.Named("Mote_ColonistFleeing"); // Or define your own custom mote

            MoteMaker.ThrowExplosionInteriorMote(loc, map, moteDef);
        }

        public static void LogAllAirtightStructures(Map map)
        {
            foreach (var building in map.listerBuildings.allBuildingsColonist)
            {
                if (building is Building b && b.IsAirtight)
                {
                    Log.Message($"[Decompression] Airtight structure: {b.def.defName} at {b.Position}");
                }
            }
        }


        public static void ClearFlushedRooms()
        {
            flushedRooms.Clear();
        }
    }

    public static class DecompressionSettings
    {
        public static bool VerboseRoomLogging = false; // Set to true for debugging
    }

    public static class RoomReflection
    {
        private static readonly FieldInfo vacuumField = typeof(Room).GetField("vacuum", BindingFlags.NonPublic | BindingFlags.Instance);

        public static float GetUnsanitizedVacuum(Room room)
        {
            return vacuumField != null ? (float)vacuumField.GetValue(room) : 0f;
        }
    }

    [HarmonyPatch(typeof(VacuumComponent), "MergeRoomsIntoGroups")]
    public static class Patch_MergeRoomsIntoGroups_LogRooms
    {
        public static void Prefix(VacuumComponent __instance)
        {
            if (!DecompressionSettings.VerboseRoomLogging) return;

            var rooms = __instance.map.regionGrid.AllRooms;
            Log.Message($"[Decompression] MergeRoomsIntoGroups scanning {rooms.Count} rooms.");
            foreach (var room in rooms)
            {
                Log.Message($"[Decompression] Room ID {room.ID}, IsDoorway: {room.IsDoorway}, ExposedToSpace: {room.ExposedToSpace}");
            }
        }
    }
    [HarmonyPatch(typeof(MapComponent), nameof(MapComponent.MapComponentTick))]
    public static class Patch_MapComponentTick_ForceDelayedDirty
    {
        private static bool dirtyForced = false;

        public static void Postfix(MapComponent __instance)
        {
            if (!dirtyForced && Find.TickManager.TicksGame > 500)
            {
                var vacuumComp = VacuumHelper.GetVacuumComponent(__instance.map);
                vacuumComp?.GetType().GetMethod("Dirty", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(vacuumComp, null);
                Log.Message("[Decompression] Forced Dirty() after 500 ticks.");
                dirtyForced = true;
            }
        }
    }

    [HarmonyPatch(typeof(VacuumComponent), "ExchangeRoomVacuum")]
    public static class Patch_ExchangeRoomVacuum_BreachDetection
    {
        public static void Postfix(VacuumComponent __instance)
        {
            var map = __instance.map;
            var roomGroupsField = typeof(VacuumComponent).GetField("roomGroups", BindingFlags.NonPublic | BindingFlags.Instance);
            var roomGroups = roomGroupsField?.GetValue(__instance) as Dictionary<Room, object>;

            if (roomGroups == null)
                return;

            foreach (var kvp in roomGroups)
            {
                Room room = kvp.Key;
                var groupData = kvp.Value;
                var groupType = groupData.GetType();

                var hasPathField = groupType.GetField("hasDirectPathToVacuum", BindingFlags.NonPublic | BindingFlags.Instance);
                var warningsField = groupType.GetField("directWarnings", BindingFlags.NonPublic | BindingFlags.Instance);

                bool hasPath = hasPathField != null && (bool)hasPathField.GetValue(groupData);
                var warnings = warningsField?.GetValue(groupData) as HashSet<IntVec3>;

                if (!hasPath || warnings == null)
                    continue;

                float vacuumLevel = room.Vacuum;

                foreach (var cell in warnings)
                {
                    if (!AtmosphereUtility.IsBreachCell(cell, map))
                    {
                        AtmosphereUtility.MarkBreach(cell, map, vacuumLevel);
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(VacuumComponent), "ExchangeRoomVacuum")]
    public static class Patch_ExchangeRoomVacuum_FlushDetection
    {
        private static readonly Dictionary<Room, float> previousVacuumLevels = new Dictionary<Room, float>();

        public static void Prefix(VacuumComponent __instance)
        {
            previousVacuumLevels.Clear();
            foreach (var room in __instance.map.regionGrid.AllRooms)
            {
                previousVacuumLevels[room] = RoomReflection.GetUnsanitizedVacuum(room);
            }
        }

        public static void Postfix(VacuumComponent __instance)
        {
            foreach (var room in __instance.map.regionGrid.AllRooms)
            {
                if (!previousVacuumLevels.TryGetValue(room, out var previous))
                    continue;

                float current = RoomReflection.GetUnsanitizedVacuum(room);
                float delta = current - previous;

                if (delta > 0.2f && current > 0.5f)
                {
                    VacuumBreachUtility.OnRoomFlushed(room, current, delta);
                }
            }
        }
    }

    [HarmonyPatch(typeof(VacuumComponent), "RebuildData")] // doors
    public static class Patch_RebuildData_ClearBreaches
    {
        public static void Prefix(VacuumComponent __instance)
        {
            var map = __instance.map;
            AtmosphereUtility.ClearBreaches(map);
            VacuumBreachUtility.ClearFlushedRooms();
        }
    }

    [HarmonyPatch(typeof(VacuumComponent), "RebuildData")] // walls
    public static class Patch_RebuildData_FlushFromWallRemoval
    {
        private static Dictionary<IntVec3, float> previousVacuumByCell = new Dictionary<IntVec3, float>();

        public static void Prefix(VacuumComponent __instance)
        {
            previousVacuumByCell.Clear();
            foreach (var room in __instance.map.regionGrid.AllRooms)
            {
                float vacuum = RoomReflection.GetUnsanitizedVacuum(room);
                foreach (var cell in room.Cells)
                {
                    previousVacuumByCell[cell] = vacuum;
                }
            }
        }

        private static int lastFlushTick = -9999;
        private const int flushCooldown = 30;

        public static void Postfix(VacuumComponent __instance)
        {
            if (Find.TickManager.TicksGame < 1000 || Find.TickManager.TicksGame - lastFlushTick < flushCooldown)
                return;

            lastFlushTick = Find.TickManager.TicksGame;

            HashSet<IntVec3> seen = new HashSet<IntVec3>();
            foreach (var room in __instance.map.regionGrid.AllRooms)
            {
                float current = RoomReflection.GetUnsanitizedVacuum(room);
                foreach (var cell in room.Cells)
                {
                    if (seen.Contains(cell)) continue;
                    seen.Add(cell);

                    if (previousVacuumByCell.TryGetValue(cell, out var oldVacuum))
                    {
                        float delta = current - oldVacuum;
                        if (delta > 0.2f && current > 0.5f)
                        {
                            VacuumBreachUtility.OnRoomFlushed(room, current, delta);
                            break;
                        }
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(RoofGrid), "SetRoof")]
    public static class Patch_SetRoof_FlushDetection
    {
        public static void Prefix(IntVec3 c, RoofDef def, RoofGrid __instance)
        {
            if (Scribe.mode != LoadSaveMode.Inactive)
                return;

            Map map = Traverse.Create(__instance).Field("map").GetValue<Map>();
            if (!map.regionAndRoomUpdater.Enabled)
                return;

            if (def == null)
            {
                Room room = c.GetRoom(map);
                if (room == null || room.TouchesMapEdge || room.RegionCount == 0)
                    return;

                if (!room.ExposedToSpace)
                {
                    float vacuum = RoomReflection.GetUnsanitizedVacuum(room);
                    if (def == null && vacuum < 0.2f) // Room is sealed and mostly pressurized
                    {
                        AtmosphereUtility.MarkBreach(c, map, vacuum);
                        VacuumBreachUtility.OnRoomFlushed(room, vacuum, 1f - vacuum); // Simulate full breach
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(MapComponent), nameof(MapComponent.MapComponentTick))]
    public static class Patch_MapComponentTick_ProcessEffects
    {
        public static void Postfix(MapComponent __instance)
        {
            VacuumBreachUtility.ProcessEffectQueue();
        }
    }

    [HarmonyPatch(typeof(Thing), "DeSpawn")]
    public static class Patch_DeSpawn_MarkBreach
    {
        public static void Prefix(Thing __instance)
        {
            Building building = __instance as Building;
            if (building == null || !building.Spawned || building.Map == null)
                return;

            if (!building.ExchangeVacuum)
                return;

            Map map = building.Map;
            VacuumComponent vacuumComp = VacuumHelper.GetVacuumComponent(map);
            if (vacuumComp == null) return;

            MethodInfo hasPathMethod = typeof(VacuumComponent).GetMethod("HasDirectPathToVacuum", BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (IntVec3 cell in building.OccupiedRect())
            {
                Room room = cell.GetRoom(map);
                if (room == null || room.TouchesMapEdge || room.RegionCount == 0)
                    continue;

                object[] args = new object[] { room, null };
                bool hasPath = hasPathMethod != null && (bool)hasPathMethod.Invoke(vacuumComp, args);
                HashSet<IntVec3> warnings = args[1] as HashSet<IntVec3>;

                if (hasPath && warnings != null && warnings.Contains(cell))
                {
                    float vacuum = RoomReflection.GetUnsanitizedVacuum(room);
                    AtmosphereUtility.MarkBreach(cell, map, vacuum);
                    VacuumBreachUtility.OnRoomFlushed(room, vacuum, 1f - vacuum);
                    VacuumBreachUtility.QueueDecompressionEffect(cell, map, vacuum);
                }
            }
        }
    }
} */

/* public class CompProperties_DecompressionWatcher : CompProperties
{
    public CompProperties_DecompressionWatcher()
    {
        this.compClass = typeof(CompDecompressionWatcher);
    }
}

/* Attaches to Door 
public class CompDecompressionWatcher : ThingComp
{
    public Building_Door ParentDoor => this.parent as Building_Door;

    private bool initialized = false;

    public override void CompTick()
    {
        base.CompTick();

        if (!initialized)
        {
            if (Find.TickManager.TicksGame < 1000) return; // Skip first few ticks
            initialized = true;
            return;
        }
    }
}

// NOTE: "DoorOpen" is a private method in Building_Door. Do not replace with anything else.
[HarmonyPatch(typeof(Building_Door), "DoorOpen")]
public static class Patch_Building_DoorOpen
{
    private static Dictionary<Building_Door, int> lastTriggerTick = new Dictionary<Building_Door, int>();

    public static void Postfix(Building_Door __instance)
    {
        int currentTick = Find.TickManager.TicksGame;
        if (lastTriggerTick.TryGetValue(__instance, out int lastTick) && currentTick - lastTick < 10)
            return;

        lastTriggerTick[__instance] = currentTick;
    
        if (__instance == null || !__instance.Spawned || __instance.Map == null || !__instance.Open)
            return;

        Log.Message("[Decompression] Door opened, checking for breach...");

        Map map = __instance.Map;
        IntVec3 doorPos = __instance.Position;
        Room doorRoom = doorPos.GetRoom(map);
        float doorVacuum = doorPos.GetVacuum(map);

        Room pressurizedRoom = null;
        foreach (IntVec3 dir in GenAdj.CardinalDirections)
        {
            IntVec3 target = doorPos + dir;
            if (!target.InBounds(map) || target.Fogged(map)) continue;

            float vacuum = target.GetVacuum(map);
            if (vacuum > VacuumUtility.MinVacuumForDamage) continue;

            Room candidate = target.GetRoom(map);
            if (candidate != null && candidate.Cells.Count() > 1)
            {
                pressurizedRoom = candidate;
                break;
            }
        }

        IntVec3? bestBreachPos = null;
        float maxDelta = 0f;

        foreach (IntVec3 adjCell in GenAdj.CardinalDirections)
        {
            IntVec3 targetCell = doorPos + adjCell;
            if (!targetCell.InBounds(map) || targetCell.Fogged(map)) continue;

            float targetVacuum = targetCell.GetVacuum(map);
            float delta = Math.Abs(doorVacuum - targetVacuum);

            if (float.IsNaN(targetVacuum) || targetVacuum < 0f || targetVacuum > 1.1f)
                continue;

            Room targetRoom = targetCell.GetRoom(map);
            if (DecompressionUtility.HasPressureDifferential(doorRoom, targetRoom) && delta > VacuumUtility.MinVacuumForDamage)
            {
                if (delta > maxDelta)
                {
                    maxDelta = delta;
                    bestBreachPos = targetCell;
                }
            }
        }

        if (bestBreachPos.HasValue)
        {
            Log.Warning($"[Decompression] Pressure differential detected (Δ={maxDelta}) between {doorPos} and {bestBreachPos.Value}. Triggering!");
            DecompressionUtility.TriggerBreach(bestBreachPos.Value, map, maxDelta, pressurizedRoom);
        }
        else
        {
            Log.Message("[Decompression] No valid breach direction found.");
        }
    }
}
*/
