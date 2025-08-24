using RimWorld;
using System;
using System.Linq;
using UnityEngine;
using Verse;

namespace DecompressionMod
{
    /// <summary>
    /// A specialized projectile that renders as furniture and reinstalls itself at the destination
    /// </summary>
    [StaticConstructorOnStartup]
    public class FurnitureProjectile : Projectile
    {
        private MinifiedThing minifiedFurniture;
        private Building originalBuilding;
        private Graphic furnitureGraphic;
        private Vector2 furnitureDrawSize = Vector2.one;
        private Color furnitureColor = Color.white;
        private Color furnitureColorTwo = Color.white;
        private Rot4 furnitureRotation = Rot4.North;
        private float spinRate = 0f;
        private bool hasValidFurniture = false;
        private Material cachedMaterial;
        private string furnitureDefName = "unknown";

        public override Material DrawMat
        {
            get
            {
                if (cachedMaterial != null)
                {
                    return cachedMaterial;
                }

                // Try to get furniture material
                if (furnitureGraphic?.MatSingle != null)
                {
                    cachedMaterial = furnitureGraphic.MatSingle;
                    return cachedMaterial;
                }

                // Fallback to base material
                return base.DrawMat;
            }
        }

        public override Vector3 ExactPosition
        {
            get
            {
                // Use the standard projectile arc calculation
                Vector3 vector = (destination - origin).Yto0() * DistanceCoveredFraction;
                float arcHeight = def.projectile.arcHeightFactor * GenMath.InverseParabola(DistanceCoveredFraction);
                return origin.Yto0() + vector + Vector3.up * (def.altitudeLayer.AltitudeFor() + arcHeight);
            }
        }

        public override Quaternion ExactRotation
        {
            get
            {
                if (spinRate != 0f)
                {
                    // Spinning furniture effect
                    float spinAngle = (Find.TickManager.TicksGame * spinRate) % 360f;
                    return Quaternion.AngleAxis(spinAngle, Vector3.up);
                }
                else
                {
                    // Keep original rotation but maybe add slight tumbling
                    Vector3 direction = (destination - origin).normalized;
                    float tumbleAngle = DistanceCoveredFraction * 45f; // Slight tumble effect
                    return Quaternion.LookRotation(direction) * Quaternion.AngleAxis(tumbleAngle, Vector3.right);
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref minifiedFurniture, "minifiedFurniture");
            Scribe_References.Look(ref originalBuilding, "originalBuilding");
            Scribe_Values.Look(ref furnitureDrawSize, "furnitureDrawSize", Vector2.one);
            Scribe_Values.Look(ref furnitureColor, "furnitureColor", Color.white);
            Scribe_Values.Look(ref furnitureColorTwo, "furnitureColorTwo", Color.white);
            Scribe_Values.Look(ref furnitureRotation, "furnitureRotation", Rot4.North);
            Scribe_Values.Look(ref spinRate, "spinRate", 0f);
            Scribe_Values.Look(ref hasValidFurniture, "hasValidFurniture", false);
            Scribe_Values.Look(ref furnitureDefName, "furnitureDefName", "unknown");
        }

        public void Initialize(MinifiedThing minified, Building original, IntVec3 targetCell, float severity)
        {
            this.minifiedFurniture = minified;
            this.originalBuilding = original;
            this.hasValidFurniture = minified != null && original != null;

            // Extract visual information from the original building
            if (original?.def?.graphicData != null)
            {
                try
                {
                    furnitureDefName = original.def.defName;
                    furnitureGraphic = original.Graphic;
                    furnitureDrawSize = original.def.graphicData.drawSize;
                    furnitureColor = original.DrawColor;
                    furnitureColorTwo = original.DrawColorTwo;
                    furnitureRotation = original.Rotation;

                    // Add some randomness based on severity
                    spinRate = severity > 0.7f ? Rand.Range(2f, 8f) : Rand.Range(0f, 3f);

                    Log.Message($"[FurnitureProjectile] Successfully initialized with {furnitureDefName}");
                }
                catch (Exception ex)
                {
                    Log.Warning($"[FurnitureProjectile] Error extracting graphics from {original.def.defName}: {ex}");
                    hasValidFurniture = false;
                }
            }
            else
            {
                Log.Warning($"[FurnitureProjectile] Invalid furniture data for projectile");
                hasValidFurniture = false;
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (!hasValidFurniture || furnitureGraphic == null)
            {
                base.DrawAt(drawLoc, flip);
                return;
            }

            // Calculate arc height for shadow positioning
            float arcHeight = def.projectile.arcHeightFactor * GenMath.InverseParabola(DistanceCoveredFraction);

            // Draw shadow on ground (only if close enough to ground)
            if (def.projectile.shadowSize > 0f && arcHeight < 10f)
            {
                DrawFurnitureShadow(drawLoc - Vector3.up * arcHeight, arcHeight);
            }

            // Draw the furniture at its arc position
            try
            {
                Quaternion rotation = ExactRotation;

                // Scale slightly based on arc height for perspective effect
                float perspectiveScale = Mathf.Lerp(0.8f, 1.2f, Mathf.Clamp01(arcHeight / 5f));
                Vector3 scale = new Vector3(
                    furnitureDrawSize.x * perspectiveScale,
                    1f,
                    furnitureDrawSize.y * perspectiveScale
                );

                // Use the furniture's graphic system
                Matrix4x4 matrix = Matrix4x4.TRS(drawLoc, rotation, scale);

                // Get material safely
                Material mat = DrawMat;
                if (mat != null)
                {
                    Graphics.DrawMesh(MeshPool.plane10, matrix, mat, 0);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[FurnitureProjectile] Error drawing furniture: {ex}");
                base.DrawAt(drawLoc, flip);
            }
        }

        private void DrawFurnitureShadow(Vector3 groundPos, float height)
        {
            try
            {
                // Don't render shadow if too high
                if (height > 15f) return;

                // Create a shadow that gets smaller and fainter with height
                float shadowScale = furnitureDrawSize.magnitude * Mathf.Lerp(1.2f, 0.3f, height / 15f);
                shadowScale = Mathf.Max(shadowScale, 0.2f);

                Vector3 shadowScale3D = new Vector3(shadowScale, 1f, shadowScale);
                Vector3 shadowPos = groundPos + Vector3.down * 0.02f;

                Matrix4x4 matrix = Matrix4x4.TRS(shadowPos, Quaternion.identity, shadowScale3D);

                // Use a simple shadow material - create one if needed
                Material shadowMat = GetShadowMaterial();
                if (shadowMat != null)
                {
                    Graphics.DrawMesh(MeshPool.plane10, matrix, shadowMat, 0);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[FurnitureProjectile] Error drawing shadow: {ex}");
            }
        }

        private static Material shadowMaterial;
        private Material GetShadowMaterial()
        {
            if (shadowMaterial == null)
            {
                try
                {
                    // Create a simple shadow material using an existing RimWorld texture
                    shadowMaterial = MaterialPool.MatFrom("Things/Skyfaller/SkyfallerShadowCircle", ShaderDatabase.Transparent);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[FurnitureProjectile] Could not load shadow material: {ex}");
                    // If that fails, just don't draw shadows
                    shadowMaterial = null;
                }
            }
            return shadowMaterial;
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            Log.Message($"[FurnitureProjectile] Impact at {Position} with {furnitureDefName}");

            bool furnitureHandled = false;

            if (hasValidFurniture && minifiedFurniture?.Spawned != true)
            {
                try
                {
                    // Try to reinstall the furniture at the impact location
                    if (ReinstallFurnitureAtLocation(Position))
                    {
                        furnitureHandled = true;
                        Log.Message($"[FurnitureProjectile] Successfully reinstalled {furnitureDefName} at {Position}");

                        // Apply damage to the reinstalled furniture
                        ApplyImpactDamageToReinstalledFurniture(Position);
                    }
                    else
                    {
                        // If reinstallation fails, drop the minified thing
                        DropMinifiedFurniture(Position);
                        furnitureHandled = true;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[FurnitureProjectile] Error during impact: {ex}");
                    if (!furnitureHandled)
                    {
                        DropMinifiedFurniture(Position);
                    }
                }
            }

            // Create impact effects
            CreateImpactEffects();

            base.Impact(hitThing, blockedByShield);
        }

        private void ApplyImpactDamageToReinstalledFurniture(IntVec3 landingPos)
        {
            try
            {
                // Find the reinstalled furniture at the landing position
                var furniture = landingPos.GetThingList(Map)
                    .OfType<Building>()
                    .FirstOrDefault(b => b.def.defName == furnitureDefName);

                if (furniture != null)
                {
                    // Apply impact damage based on the projectile's severity
                    float baseDamage = spinRate > 5f ? Rand.Range(15f, 35f) : Rand.Range(5f, 20f); // Use spin rate as severity indicator

                    // Reduce damage for lighter furniture types (same logic as before)
                    string defName = furniture.def.defName.ToLower();
                    float damageReduction = 1.0f;

                    if (defName.Contains("bed") || defName.Contains("sleeping"))
                    {
                        damageReduction = 0.9f;
                    }
                    else if (defName.Contains("stool") || defName.Contains("table") || defName.Contains("desk"))
                    {
                        damageReduction = 0.7f;
                    }
                    else if ((defName.Contains("plant") && defName.Contains("pot")) || defName.Contains("bonsai") || defName.Contains("planter"))
                    {
                        damageReduction = 0.5f;
                    }
                    else if (defName.Contains("lamp") || defName.Contains("torch") || defName.Contains("standing"))
                    {
                        damageReduction = 0.6f;
                    }
                    else if (defName.Contains("chair") || defName.Contains("armchair"))
                    {
                        damageReduction = 0.8f;
                    }

                    // Apply damage reduction and displacement bonus
                    baseDamage *= damageReduction * 1.5f; // 1.5x for being displaced

                    if (baseDamage > 0)
                    {
                        DamageInfo damageInfo = new DamageInfo(
                            DamageDefOf.Blunt,
                            baseDamage,
                            0f, -1f, null, null, null,
                            DamageInfo.SourceCategory.ThingOrUnknown
                        );

                        furniture.TakeDamage(damageInfo);
                        Log.Message($"[FurnitureProjectile] Applied {baseDamage:F1} impact damage to reinstalled {furnitureDefName}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[FurnitureProjectile] Error applying impact damage: {ex}");
            }
        }

        private bool ReinstallFurnitureAtLocation(IntVec3 targetCell)
        {
            if (minifiedFurniture?.GetInnerIfMinified() is Building building)
            {
                // Check if we can place the building here
                if (GenConstruct.CanPlaceBlueprintAt(building.def, targetCell, building.Rotation, Map).Accepted)
                {
                    // Spawn the building
                    GenSpawn.Spawn(building, targetCell, Map, building.Rotation);

                    // Destroy the minified version
                    if (minifiedFurniture.Spawned)
                    {
                        minifiedFurniture.Destroy(DestroyMode.Vanish);
                    }

                    return true;
                }
            }
            return false;
        }

        private void DropMinifiedFurniture(IntVec3 nearCell)
        {
            if (minifiedFurniture != null)
            {
                // Try to place the minified thing near the target
                IntVec3 dropCell;
                if (CellFinder.TryFindRandomCellNear(nearCell, Map, 3, (IntVec3 c) => c.Standable(Map), out dropCell))
                {
                    GenPlace.TryPlaceThing(minifiedFurniture, dropCell, Map, ThingPlaceMode.Near);
                    Log.Message($"[FurnitureProjectile] Dropped minified {furnitureDefName} at {dropCell}");
                }
                else
                {
                    GenPlace.TryPlaceThing(minifiedFurniture, nearCell, Map, ThingPlaceMode.Near);
                    Log.Message($"[FurnitureProjectile] Dropped minified {furnitureDefName} at original target {nearCell}");
                }
            }
        }

        private void CreateImpactEffects()
        {
            // Create dust and impact effects
            FleckMaker.ThrowDustPuffThick(Position.ToVector3(), Map, 2f, Color.gray);

            // Maybe some debris
            if (Rand.Chance(0.3f))
            {
                FleckMaker.ThrowDustPuffThick(Position.ToVector3() + Rand.InsideUnitCircleVec3 * 1f, Map, 1f, Color.white);
            }
        }
    }
}