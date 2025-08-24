using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace DecompressionMod
{
    public class DecompressionMod : Mod
    {
        public DecompressionMod(ModContentPack content) : base(content)
        {
            Log.Message("[DecompressionMod] Mod constructor called.");
        }
    }

    // Harmony bootstrap — runs once when the game loads
    [StaticConstructorOnStartup]
    public static class DecompressionHarmonyBootstrap
    {
        static DecompressionHarmonyBootstrap()
        {
            try
            {
                var id = "DecompressionMod.Core";
                var harmony = new Harmony(id);
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                Log.Message($"[DecompressionMod] Harmony patched ({id}).");
            }
            catch (Exception ex)
            {
                Log.Error($"[DecompressionMod] Harmony bootstrap failed: {ex}");
            }
        }
    }

    // Initialize AFTER the game loads to avoid conflicts
    [StaticConstructorOnStartup]
    public static class PostGameInitializer
    {
        static PostGameInitializer()
        {
            try
            {
                Log.Message("[DecompressionMod] Post-game initialization starting...");

                InitializeJobDefs();

                Log.Message("[DecompressionMod] Initialization complete.");
            }
            catch (Exception ex)
            {
                Log.Error($"[DecompressionMod] Critical initialization error: {ex}");
            }
        }

        private static void InitializeJobDefs()
        {
            try
            {
                EnsureJobDefExists("SeekOxygen", typeof(JobDriver_SeekOxygen), "seeking oxygen");

                var seekOxygenJob = DefDatabase<JobDef>.GetNamedSilentFail("SeekOxygen");
                if (seekOxygenJob != null)
                {
                    OxygenDefOf.SeekOxygen = seekOxygenJob;
                    Log.Message("[DecompressionMod] SeekOxygen job definition linked successfully.");
                }

                Log.Message("[DecompressionMod] Job definitions initialized.");
            }
            catch (Exception ex)
            {
                Log.Error($"[DecompressionMod] Error initializing job definitions: {ex}");
            }
        }

        private static void EnsureJobDefExists(string defName, Type driverClass, string reportString)
        {
            try
            {
                if (DefDatabase<JobDef>.GetNamedSilentFail(defName) == null)
                {
                    Log.Warning($"[DecompressionMod] JobDef {defName} not found in XML, creating runtime version.");

                    JobDef jobDef = new JobDef
                    {
                        defName = defName,
                        driverClass = driverClass,
                        reportString = reportString,
                        casualInterruptible = false,
                        playerInterruptible = true,
                        suspendable = false,
                        checkOverrideOnDamage = CheckJobOverrideOnDamageMode.Never,
                        alwaysShowWeapon = false
                    };

                    DefDatabase<JobDef>.Add(jobDef);
                    Log.Message($"[DecompressionMod] Created runtime JobDef: {defName}");
                }
                else
                {
                    Log.Message($"[DecompressionMod] Found existing JobDef: {defName}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[DecompressionMod] Error ensuring JobDef {defName} exists: {ex}");
            }
        }
    }

    // Ensure Decompression_Component exists on every map
    [HarmonyPatch(typeof(Map), "FinalizeInit")]
    public static class Map_FinalizeInit_AddDecompressionComponent
    {
        [HarmonyPostfix]
        public static void Postfix(Map __instance)
        {
            if (__instance.GetComponent<Decompression_Component>() == null)
            {
                __instance.components.Add(new Decompression_Component(__instance));
                Log.Message("[DecompressionMod] Injected Decompression_Component into map.");
            }
        }
    }

    // SAFER version of the vacuum component patch
    [HarmonyPatch(typeof(VacuumComponent), "ExchangeRoomVacuum")]
    public static class VacuumComponent_ExchangeRoomVacuum_Patch
    {
        private static int lastProcessedTick = -1;

        [HarmonyPostfix]
        public static void Postfix(VacuumComponent __instance)
        {
            try
            {
                int currentTick = Find.TickManager.TicksGame;
                if (currentTick == lastProcessedTick) return;
                lastProcessedTick = currentTick;

                var map = __instance.map;
                if (map == null) return;

                var decompComponent = map.GetComponent<Decompression_Component>();
                if (decompComponent == null) return;

                var currentVacuumLevels = new Dictionary<Room, float>();
                foreach (var room in map.regionGrid.AllRooms)
                {
                    if (room != null && !room.ExposedToSpace)
                    {
                        currentVacuumLevels[room] = room.Vacuum;
                    }
                }

                decompComponent.DetectDecompressionEvents(currentVacuumLevels);
            }
            catch (Exception ex)
            {
                if (Prefs.DevMode)
                {
                    Log.Warning($"[DecompressionMod] VacuumComponent patch error: {ex.Message}");
                }
            }
        }
    }

    // SAFER version of the pawn spawning patch
    [HarmonyPatch(typeof(Pawn), "SpawnSetup")]
    public static class Patch_Pawn_SpawnSetup_AddOxygenComponent
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance)
        {
            try
            {
                if (__instance?.RaceProps?.Humanlike == true)
                {
                    var existingComp = __instance.GetComp<CompOxygenTracker>();
                    if (existingComp == null)
                    {
                        var comp = new CompOxygenTracker();
                        comp.parent = __instance;
                        comp.Initialize(new CompProperties_OxygenTracker());
                        __instance.AllComps.Add(comp);

                        if (Prefs.DevMode)
                        {
                            Log.Message($"[DecompressionMod] Added oxygen component to {__instance.Name}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (Prefs.DevMode)
                {
                    Log.Warning($"[DecompressionMod] Error adding oxygen component to {__instance?.Name}: {ex.Message}");
                }
            }
        }
    }
}