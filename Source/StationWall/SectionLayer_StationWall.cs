using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

// WORKING!!!!!!!!!!!!!!!
namespace RimWorld
{
    public class SectionLayer_StationWall : SectionLayer
    {
        // Safe way to get StationWall ThingDef
        private static ThingDef _stationWallDef;
        private static ThingDef StationWallDef
        {
            get
            {
                if (_stationWallDef == null)
                {
                    _stationWallDef = DefDatabase<ThingDef>.GetNamedSilentFail("StationWall");
                    if (_stationWallDef == null)
                    {
                        Log.Warning("[StationWall] StationWall ThingDef not found!");
                    }
                }
                return _stationWallDef;
            }
        }

        public enum CornerType
        {
            None,
            Corner_NW, Corner_NE, Corner_SW, Corner_SE,
            Diagonal_NW, Diagonal_NE, Diagonal_SW, Diagonal_SE
        }

        #region Constants and Static Data
        private const float HullCornerScale = 2f; // Back to original scale
        private const int BakedIndoorQueue = 3185;

        private static readonly Vector2[] UVs = {
        new Vector2(0f, 0f),
        new Vector2(0f, 1f),
        new Vector2(1f, 1f),
        new Vector2(1f, 0f)
    };

        // All overlay texture paths for StationWall
        private static readonly Dictionary<CornerType, string> TexPaths = new Dictionary<CornerType, string>
    {
        { CornerType.Corner_NW,   "Things/Building/Linked/StationWall/CornerOverlay/CornerFull_NW" },
        { CornerType.Corner_NE,   "Things/Building/Linked/StationWall/CornerOverlay/CornerFull_NE" },
        { CornerType.Corner_SW,   "Things/Building/Linked/StationWall/CornerOverlay/CornerFull_SW" },
        { CornerType.Corner_SE,   "Things/Building/Linked/StationWall/CornerOverlay/CornerFull_SE" },
        { CornerType.Diagonal_NW, "Things/Building/Linked/StationWall/CornerOverlay/CornerPartial_NW" },
        { CornerType.Diagonal_NE, "Things/Building/Linked/StationWall/CornerOverlay/CornerPartial_NE" },
        { CornerType.Diagonal_SW, "Things/Building/Linked/StationWall/CornerOverlay/CornerPartial_SW" },
        { CornerType.Diagonal_SE, "Things/Building/Linked/StationWall/CornerOverlay/CornerPartial_SE" }
    };

        // Lazy‐initialized materials
        private static readonly Dictionary<CornerType, CachedMaterial> Materials = new Dictionary<CornerType, CachedMaterial>();

        private static readonly IntVec3[] Directions = {
        IntVec3.North,      // 0
        IntVec3.East,       // 1  
        IntVec3.South,      // 2
        IntVec3.West,       // 3
        IntVec3.North + IntVec3.West, // 4 - NW
        IntVec3.North + IntVec3.East, // 5 - NE
        IntVec3.South + IntVec3.East, // 6 - SE
        IntVec3.South + IntVec3.West  // 7 - SW
    };

        private static readonly int[][] DirectionPairs = {
        new[] { 0, 2 }, // North-South
        new[] { 1, 3 }, // East-West
        new[] { 4, 6 }, // NW-SE diagonal
        new[] { 5, 7 }  // NE-SW diagonal
    };

        private static readonly bool[] tmpChecks = new bool[Directions.Length];
        #endregion

        #region Properties
        private static Shader WallShader => ShaderDatabase.CutoutOverlay;
        private static readonly float CornerAltitude = AltitudeLayer.BuildingOnTop.AltitudeFor();
        private static readonly float BakedAltitude = AltitudeLayer.MetaOverlays.AltitudeFor();

        public override bool Visible => ModsConfig.OdysseyActive;
        #endregion

        public SectionLayer_StationWall(Section section) : base(section)
        {
            Log.Warning("[StationWall] SectionLayer_StationWall constructor called!");
            relevantChangeTypes =
                MapMeshFlagDefOf.Buildings |
                MapMeshFlagDefOf.Terrain |
                MapMeshFlagDefOf.Things |
                MapMeshFlagDefOf.Roofs;
        }

        #region Material Management
        // Get or create a CachedMaterial for a given corner type
        private static CachedMaterial GetMaterial(CornerType type)
        {
            if (!Materials.TryGetValue(type, out var cm))
            {
                cm = new CachedMaterial(TexPaths[type], WallShader);
                Materials[type] = cm;
            }
            return cm;
        }
        #endregion

        #region Indoor/Floor Detection Logic
        /// <summary>
        /// Determines if a cell should be considered "indoor" (preventing corner rendering).
        /// For Station Walls: roofed areas OR constructed floors should hide corners.
        /// </summary>
        private static bool IsIndoorMasked(IntVec3 cell, Map map)
        {
            // Always hide corners under roofs
            if (cell.Roofed(map))
                return true;

            // Hide corners on constructed/artificial floors
            var terrain = map.terrainGrid.TerrainAt(cell);
            if (terrain?.tags != null && terrain.tags.Contains("Floor"))
                return true;

            // Allow corners on natural terrain (dirt, rock, etc.)
            return false;
        }

        /// <summary>
        /// Checks if a corner should be masked based on the detection cell itself.
        /// Floor detection always happens at the detection position, regardless of visual offset.
        /// </summary>
        private static bool IsCornerIndoorMasked(IntVec3 detectionCell, CornerType cornerType, Map map)
        {
            // Always check for floors at the detection cell, not the visual render position
            return IsIndoorMasked(detectionCell, map);
        }
        #endregion

        #region Corner Positioning
        /// <summary>
        /// Gets the offset for where the corner overlay should be rendered relative to the detection cell.
        /// Each corner type needs its own specific offset to center properly.
        /// </summary>
        private static IntVec3 GetOffset(CornerType cornerType)
        {
            switch (cornerType)
            {
                case CornerType.Corner_NE:
                case CornerType.Diagonal_NE:
                    return new IntVec3(0, 0, 0);   // NE: no offset needed
                case CornerType.Corner_NW:
                case CornerType.Diagonal_NW:
                    return new IntVec3(-1, 0, 0);  // NW: shift west by 1
                case CornerType.Corner_SE:
                case CornerType.Diagonal_SE:
                    return new IntVec3(0, 0, -1);  // SE: shift south by 1
                case CornerType.Corner_SW:
                case CornerType.Diagonal_SW:
                    return new IntVec3(-1, 0, -1); // SW: shift west and south by 1
                default:
                    return IntVec3.Zero;
            }
        }
        #endregion

        #region Mesh Generation
        private static void AddQuad(LayerSubMesh sm, Vector3 center, float scale, float altitude, Color color)
        {
            int start = sm.verts.Count;
            for (int i = 0; i < 4; i++)
            {
                sm.verts.Add(new Vector3(center.x + UVs[i].x * scale, altitude, center.z + UVs[i].y * scale));
                sm.uvs.Add(UVs[i]);
                sm.colors.Add(color);
            }
            sm.tris.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }

        private void AddQuad(
            Material mat,
            IntVec3 cell,
            float scale,
            float altitude,
            Color color,
            bool addGravshipMask,
            bool addIndoorMask)
        {
            // Main overlay
            var sm0 = GetSubMesh(mat);
            AddQuad(sm0, cell.ToVector3(), scale, altitude, color);

            // Gravship‐style mask (if needed for compatibility)
            if (addGravshipMask)
            {
                var src = sm0.material.mainTexture as Texture2D;
                var m = MaterialPool.MatFrom(src, ShaderDatabase.GravshipMaskMasked, sm0.material.color);
                var sm1 = GetSubMesh(m);
                AddQuad(sm1, cell.ToVector3(), scale, altitude, color);
            }

            // Indoor mask
            if (addIndoorMask)
            {
                var src = sm0.material.mainTexture as Texture2D;
                var m = MaterialPool.MatFrom(src, ShaderDatabase.IndoorMaskMasked, sm0.material.color);
                var sm2 = GetSubMesh(m);
                AddQuad(sm2, cell.ToVector3(), scale, altitude, color);
            }
        }
        #endregion

        #region Main Regeneration
        /// <summary>
        /// Main SectionLayer regeneration - follows Gravship Hull pattern exactly
        /// but uses StationWall detection and floor-based masking
        /// </summary>
        public override void Regenerate()
        {
            //Log.Warning("[StationWall] Regenerate() called!");

            if (!ModsConfig.OdysseyActive)
            {
                //Log.Warning("[StationWall] Odyssey not active, skipping");
                return;
            }

            ClearSubMeshes(MeshParts.All);

            var map = base.Map;
            var grid = map.terrainGrid;
            int cornerCount = 0;

            foreach (var cell in section.CellRect)
            {
                if (ShouldDrawCornerPiece(cell, map, grid, out var cornerType, out var color))
                {
                    cornerCount++;
                    var material = GetMaterial(cornerType).Material;
                    var offset = GetOffset(cornerType);
                    bool indoor = IsCornerIndoorMasked(cell, cornerType, map);

                    //Log.Warning($"[StationWall] Found corner {cornerType} at {cell}, offset {offset}, indoor: {indoor}");

                    // Only draw if not completely masked by indoor conditions
                    if (!indoor)
                    {
                        AddQuad(
                            material,
                            cell + offset,
                            HullCornerScale,
                            CornerAltitude,
                            color,
                            addGravshipMask: false, // Station walls don't need gravship masks
                            addIndoorMask: false    // We already filtered out indoor cases
                        );
                    }
                }
            }

            //Log.Warning($"[StationWall] Found {cornerCount} total corners in this section");
            FinalizeMesh(MeshParts.All);
        }
        #endregion

        #region Baked Indoor Mesh (for minimap)
        /// <summary>
        /// Indoor‐only mesh (baked) for minimap/meta overlays
        /// </summary>
        public static List<LayerSubMesh> BakeStationWallIndoorMesh(Map map, CellRect bounds, Vector3 center)
        {
            var meshes = new Dictionary<CornerType, LayerSubMesh>();
            var terrainGrid = map.terrainGrid;

            foreach (var cell in bounds)
            {
                if (ShouldDrawCornerPiece(cell, map, terrainGrid, out var cornerType, out var color)
                    && IsCornerIndoorMasked(cell, cornerType, map))
                {
                    var baseMat = GetMaterial(cornerType).Material;
                    var srcTex = baseMat.mainTexture as Texture2D;
                    var mat = MaterialPool.MatFrom(srcTex, ShaderDatabase.IndoorMaskMasked, baseMat.color, BakedIndoorQueue);
                    var offset = GetOffset(cornerType);

                    if (!meshes.TryGetValue(cornerType, out var subMesh))
                        meshes[cornerType] = subMesh = MapDrawLayer.CreateFreeSubMesh(mat, map);

                    AddQuad(subMesh, (cell + offset).ToVector3() - center, HullCornerScale, BakedAltitude, color);
                }
            }

            foreach (var subMesh in meshes.Values)
                subMesh.FinalizeMesh(MeshParts.All);

            return meshes.Values.ToList();
        }
        #endregion

        #region Corner Detection Logic
        /// <summary>
        /// Checks if a specific corner type should be drawn at a given position
        /// </summary>
        public static bool ShouldDrawSpecificCorner(
            IntVec3 pos,
            Map map,
            TerrainGrid terrGrid,
            CornerType cornerType,
            out Color color)
        {
            color = Color.white;

            // Don't draw on cells that have buildings
            if (pos.GetEdifice(map) != null)
                return false;

            // Don't draw on substructure (this is the key difference from Gravship Hull)
            var foundation = terrGrid.FoundationAt(pos);
            if (foundation?.IsSubstructure ?? false)
                return false;

            // Check all 8 directions for StationWall presence
            for (int i = 0; i < Directions.Length; i++)
                tmpChecks[i] = (pos + Directions[i]).GetEdificeSafe(map)?.def == StationWallDef;

            // Check if this specific corner type pattern matches
            bool patternMatches = false;
            switch (cornerType)
            {
                case CornerType.Corner_NW:
                    patternMatches = tmpChecks[0] && tmpChecks[3] && !tmpChecks[1] && !tmpChecks[2] && tmpChecks[4];
                    break;
                case CornerType.Diagonal_NW:
                    patternMatches = tmpChecks[0] && tmpChecks[3] && !tmpChecks[1] && !tmpChecks[2] && !tmpChecks[4];
                    break;
                case CornerType.Corner_NE:
                    patternMatches = tmpChecks[0] && tmpChecks[1] && !tmpChecks[2] && !tmpChecks[3] && tmpChecks[5];
                    break;
                case CornerType.Diagonal_NE:
                    patternMatches = tmpChecks[0] && tmpChecks[1] && !tmpChecks[2] && !tmpChecks[3] && !tmpChecks[5];
                    break;
                case CornerType.Corner_SE:
                    patternMatches = tmpChecks[2] && tmpChecks[1] && !tmpChecks[0] && !tmpChecks[3] && tmpChecks[6];
                    break;
                case CornerType.Diagonal_SE:
                    patternMatches = tmpChecks[2] && tmpChecks[1] && !tmpChecks[0] && !tmpChecks[3] && !tmpChecks[6];
                    break;
                case CornerType.Corner_SW:
                    patternMatches = tmpChecks[2] && tmpChecks[3] && !tmpChecks[0] && !tmpChecks[1] && tmpChecks[7];
                    break;
                case CornerType.Diagonal_SW:
                    patternMatches = tmpChecks[2] && tmpChecks[3] && !tmpChecks[0] && !tmpChecks[1] && !tmpChecks[7];
                    break;
            }

            if (!patternMatches)
                return false;

            // Get color from relevant adjacent walls for this corner type
            var relevantDirections = GetRelevantDirectionsForCorner(cornerType);
            foreach (var dirIndex in relevantDirections)
            {
                if (tmpChecks[dirIndex])
                {
                    var wallCell = pos + Directions[dirIndex];
                    var wall = wallCell.GetEdificeSafe(map);
                    if (wall != null)
                    {
                        color = wall.DrawColor;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Gets the relevant direction indices for a corner type (for color determination)
        /// </summary>
        private static int[] GetRelevantDirectionsForCorner(CornerType cornerType)
        {
            switch (cornerType)
            {
                case CornerType.Corner_NW:
                case CornerType.Diagonal_NW:
                    return new[] { 0, 3 }; // North, West
                case CornerType.Corner_NE:
                case CornerType.Diagonal_NE:
                    return new[] { 0, 1 }; // North, East
                case CornerType.Corner_SE:
                case CornerType.Diagonal_SE:
                    return new[] { 2, 1 }; // South, East
                case CornerType.Corner_SW:
                case CornerType.Diagonal_SW:
                    return new[] { 2, 3 }; // South, West
                default:
                    return new int[0];
            }
        }

        /// <summary>
        /// Legacy method for compatibility - now just finds the first valid corner type
        /// </summary>
        public static bool ShouldDrawCornerPiece(
            IntVec3 pos,
            Map map,
            TerrainGrid terrGrid,
            out CornerType cornerType,
            out Color color)
        {
            cornerType = CornerType.None;
            color = Color.white;

            // Check each corner type in order (for legacy compatibility)
            var cornerTypes = new[]
            {
            CornerType.Corner_NW, CornerType.Diagonal_NW,
            CornerType.Corner_NE, CornerType.Diagonal_NE,
            CornerType.Corner_SE, CornerType.Diagonal_SE,
            CornerType.Corner_SW, CornerType.Diagonal_SW
        };

            foreach (var ct in cornerTypes)
            {
                if (ShouldDrawSpecificCorner(pos, map, terrGrid, ct, out color))
                {
                    cornerType = ct;
                    return true;
                }
            }

            return false;
        }
        #endregion
    }
}