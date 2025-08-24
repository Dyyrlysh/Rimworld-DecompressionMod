using RimWorld;
using System;
using System.Linq;
using UnityEngine;
using Verse;

namespace DecompressionMod
{
    /// <summary>
    /// Utility class for launching furniture as projectiles during decompression events
    /// </summary>
    [StaticConstructorOnStartup]
    public static class FurnitureLauncher
    {
        private static ThingDef furnitureProjectileDef;

        /// <summary>
        /// Gets or creates the furniture projectile ThingDef
        /// </summary>
        public static ThingDef FurnitureProjectileDef
        {
            get
            {
                if (furnitureProjectileDef == null)
                {
                    // Try to find the XML-defined projectile first
                    furnitureProjectileDef = DefDatabase<ThingDef>.GetNamedSilentFail("FurnitureProjectile_Decompression");

                    if (furnitureProjectileDef == null)
                    {
                        Log.Warning("[FurnitureLauncher] XML ThingDef not found, creating runtime version");
                        furnitureProjectileDef = CreateFurnitureProjectileDef();
                    }
                    else
                    {
                        Log.Message("[FurnitureLauncher] Found XML ThingDef for furniture projectile");
                    }
                }
                return furnitureProjectileDef;
            }
        }

        /// <summary>
        /// Launch a piece of furniture as a projectile towards a target location
        /// </summary>
        public static bool LaunchFurnitureProjectile(Building furniture, IntVec3 targetCell, DecompressionEvent decompressionEvent)
        {
            if (furniture?.Spawned != true || furniture.Map == null)
            {
                Log.Warning("[FurnitureLauncher] Cannot launch invalid furniture");
                return false;
            }

            // Safety check: ensure target is valid
            if (!targetCell.InBounds(furniture.Map))
            {
                Log.Warning($"[FurnitureLauncher] Target cell {targetCell} is out of bounds");
                return false;
            }

            try
            {
                Map map = furniture.Map;
                IntVec3 startCell = furniture.Position;

                // Store furniture info BEFORE minifying (since minifying destroys the original)
                string furnitureName = furniture.def.defName;

                // Create minified version of the furniture to preserve all data
                MinifiedThing minifiedThing = furniture.MakeMinified();
                if (minifiedThing == null)
                {
                    Log.Warning($"[FurnitureLauncher] Failed to minify {furnitureName}");
                    return false;
                }

                // Create and configure the projectile
                FurnitureProjectile projectile = (FurnitureProjectile)ThingMaker.MakeThing(FurnitureProjectileDef);

                // Initialize with stored data since original building is now destroyed
                projectile.Initialize(minifiedThing, furniture, targetCell, decompressionEvent.pressureDelta);

                // Launch the projectile
                Vector3 startPos = startCell.ToVector3Shifted() + Vector3.up * 0.5f;

                GenSpawn.Spawn(projectile, startCell, map);
                projectile.Launch(
                    launcher: null,
                    origin: startPos,
                    usedTarget: new LocalTargetInfo(targetCell),
                    intendedTarget: new LocalTargetInfo(targetCell),
                    hitFlags: ProjectileHitFlags.All,
                    preventFriendlyFire: true
                );

                Log.Message($"[FurnitureLauncher] Launched {furnitureName} from {startCell} to {targetCell}");
                CreateLaunchEffects(startPos, map, decompressionEvent.severity);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[FurnitureLauncher] Error launching furniture projectile: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Creates visual and audio effects at the launch point
        /// </summary>
        private static void CreateLaunchEffects(Vector3 position, Map map, DecompressionSeverity severity)
        {
            // Dust cloud
            FleckMaker.ThrowDustPuffThick(position, map, 1.5f, Color.gray);

            // Extra effects for major decompression
            if (severity == DecompressionSeverity.Major)
            {
                // Bigger dust cloud
                FleckMaker.ThrowDustPuffThick(position + Rand.InsideUnitCircleVec3 * 0.5f, map, 2f, Color.white);

                // Maybe some sparks or debris
                for (int i = 0; i < Rand.Range(2, 4); i++)
                {
                    Vector3 sparkPos = position + Rand.InsideUnitCircleVec3 * 1f;
                    FleckMaker.ThrowDustPuffThick(sparkPos, map, 0.8f, Color.yellow);
                }
            }
        }

        /// <summary>
        /// Creates the ThingDef for furniture projectiles (fallback for runtime creation)
        /// </summary>
        private static ThingDef CreateFurnitureProjectileDef()
        {
            ThingDef def = new ThingDef
            {
                defName = "FurnitureProjectile_Decompression",
                label = "flying furniture",
                thingClass = typeof(FurnitureProjectile),
                category = ThingCategory.Projectile,
                altitudeLayer = AltitudeLayer.Projectile,
                useHitPoints = false,
                neverMultiSelect = true,
                comps = new System.Collections.Generic.List<CompProperties>()
            };

            // Graphics - use a texture that definitely exists in RimWorld
            def.graphicData = new GraphicData
            {
                texPath = "UI/Commands/Attack", // This definitely exists in RimWorld
                graphicClass = typeof(Graphic_Single),
                drawSize = Vector2.one
            };

            // Projectile properties
            def.projectile = new ProjectileProperties
            {
                speed = 8f,
                arcHeightFactor = 1.2f,
                shadowSize = 1.5f,
                flyOverhead = false,
                alwaysFreeIntercept = false,
                damageDef = DamageDefOf.Blunt
            };

            // Use reflection to set the private fields since they don't have public setters
            var damageAmountField = typeof(ProjectileProperties).GetField("damageAmountBase",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var armorPenField = typeof(ProjectileProperties).GetField("armorPenetrationBase",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            damageAmountField?.SetValue(def.projectile, 1);
            armorPenField?.SetValue(def.projectile, 0f);

            // Try to register the def properly
            try
            {
                def.PostLoad();
                def.ResolveReferences();

                Log.Message("[FurnitureLauncher] Successfully created runtime ThingDef");
            }
            catch (Exception ex)
            {
                Log.Error($"[FurnitureLauncher] Error registering runtime ThingDef: {ex}");
            }

            return def;
        }

        /// <summary>
        /// Calculate the optimal target cell for furniture displacement during decompression
        /// </summary>
        public static IntVec3 CalculateDisplacementTarget(IntVec3 furniturePos, IntVec3 breachPoint, Map map, int maxDistance = 4)
        {
            // Calculate direction TOWARD the breach (furniture gets sucked toward vacuum)
            Vector3 direction = (breachPoint - furniturePos).ToVector3().normalized;

            // If direction is zero (furniture is at breach point), use random direction
            if (direction.magnitude < 0.1f)
            {
                direction = Rand.InsideUnitCircleVec3.normalized;
            }

            Log.Message($"[FurnitureLauncher] Furniture at {furniturePos}, breach at {breachPoint}, direction: {direction}");

            // Try distances from 1 to maxDistance (furniture moves toward breach)
            for (int distance = 1; distance <= maxDistance; distance++)
            {
                IntVec3 targetCell = furniturePos + (direction * distance).ToIntVec3();

                // Check if path is clear (no walls blocking)
                if (IsValidLandingSpot(targetCell, map) && HasClearPath(furniturePos, targetCell, map))
                {
                    Log.Message($"[FurnitureLauncher] Found valid target at {targetCell} (distance {distance})");
                    return targetCell;
                }

                // Also try slight variations to account for room layout
                for (int angle = -45; angle <= 45; angle += 15)
                {
                    if (angle == 0) continue; // Skip the original direction

                    Vector3 rotatedDir = Quaternion.AngleAxis(angle, Vector3.up) * direction;
                    IntVec3 altTarget = furniturePos + (rotatedDir * distance).ToIntVec3();

                    if (IsValidLandingSpot(altTarget, map) && HasClearPath(furniturePos, altTarget, map))
                    {
                        Log.Message($"[FurnitureLauncher] Found angled target at {altTarget} (angle {angle}°)");
                        return altTarget;
                    }
                }
            }

            // If no clear path toward breach, try adjacent cells that don't cross walls
            foreach (IntVec3 adjCell in GenAdj.CellsAdjacent8Way(furniturePos, Rot4.North, IntVec2.One))
            {
                if (adjCell.InBounds(map) && IsValidLandingSpot(adjCell, map) && HasClearPath(furniturePos, adjCell, map))
                {
                    Log.Message($"[FurnitureLauncher] Using adjacent fallback: {adjCell}");
                    return adjCell;
                }
            }

            Log.Warning($"[FurnitureLauncher] Could not find valid path for furniture at {furniturePos} toward breach at {breachPoint}");
            return furniturePos; // No movement if no valid path
        }

        /// <summary>
        /// Check if there's a clear path between two points (no walls blocking)
        /// </summary>
        private static bool HasClearPath(IntVec3 start, IntVec3 end, Map map)
        {
            // Simple line-of-sight check - make sure no walls block the path
            foreach (IntVec3 cell in GenSight.PointsOnLineOfSight(start, end))
            {
                if (!cell.InBounds(map))
                {
                    return false;
                }

                // Check for walls or other impassable buildings
                Building edifice = cell.GetEdifice(map);
                if (edifice != null)
                {
                    // Allow movement through open doors
                    if (edifice is Building_Door door)
                    {
                        if (!door.Open)
                        {
                            Log.Message($"[FurnitureLauncher] Path blocked by closed door at {cell}");
                            return false;
                        }
                    }
                    // Block movement through walls and other solid buildings
                    else if (edifice.def.Fillage == FillCategory.Full)
                    {
                        Log.Message($"[FurnitureLauncher] Path blocked by {edifice.def.defName} at {cell}");
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Check if a cell is a valid landing spot for furniture
        /// </summary>
        private static bool IsValidLandingSpot(IntVec3 cell, Map map)
        {
            if (!cell.InBounds(map) || !cell.Standable(map))
                return false;

            // Check for existing buildings that would block placement
            Building existingBuilding = cell.GetEdifice(map);
            if (existingBuilding != null)
                return false;

            // Check for pawns
            if (cell.GetFirstPawn(map) != null)
                return false;

            // Check for impassable things
            var things = cell.GetThingList(map);
            foreach (var thing in things)
            {
                if (thing.def.passability == Traversability.Impassable)
                    return false;
            }

            return true;
        }
    }
}