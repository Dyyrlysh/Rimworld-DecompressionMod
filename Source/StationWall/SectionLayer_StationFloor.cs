using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimWorld
{
    public class SectionLayer_StationFloor : SectionLayer
    {
        [Flags]
        private enum EdgeDirections
        {
            None = 0,
            North = 1,
            East = 2,
            South = 4,
            West = 8
        }

        [Flags]
        private enum CornerDirections
        {
            None = 0,
            SouthWest = 1,
            NorthWest = 2,
            NorthEast = 4,
            SouthEast = 8
        }

        // Edge materials - these will be your edge textures
        private static readonly CachedMaterial EdgeFlat = new CachedMaterial("Terrain/Surfaces/StationFloor/BorderInside/StationFloorBorderInside_Flat", ShaderDatabase.Cutout);
        private static readonly CachedMaterial EdgeCornerInner = new CachedMaterial("Terrain/Surfaces/StationFloor/BorderInside/StationFloorBorderInside_CornerInner", ShaderDatabase.Cutout);
        private static readonly CachedMaterial EdgeCornerOuter = new CachedMaterial("Terrain/Surfaces/StationFloor/BorderInside/StationFloorBorderInside_CornerOuter", ShaderDatabase.Cutout);
        private static readonly CachedMaterial EdgeUShape = new CachedMaterial("Terrain/Surfaces/StationFloor/BorderInside/StationFloorBorderInside_UShape", ShaderDatabase.Cutout);
        private static readonly CachedMaterial EdgeOShape = new CachedMaterial("Terrain/Surfaces/StationFloor/BorderInside/StationFloorBorderInside_OShape", ShaderDatabase.Cutout);

        private static readonly Vector2[] UVs = new Vector2[4]
        {
            new Vector2(0f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(1f, 0f)
        };

        // Edge pattern definitions (copied from SubstructureProps)
        private static readonly Dictionary<EdgeDirections, (CachedMaterial, Rot4)[]> EdgeMats = new Dictionary<EdgeDirections, (CachedMaterial, Rot4)[]>
        {
            {
                EdgeDirections.North,
                new(CachedMaterial, Rot4)[1] { (EdgeFlat, Rot4.South) }
            },
            {
                EdgeDirections.East,
                new(CachedMaterial, Rot4)[1] { (EdgeFlat, Rot4.West) }
            },
            {
                EdgeDirections.South,
                new(CachedMaterial, Rot4)[1] { (EdgeFlat, Rot4.North) }
            },
            {
                EdgeDirections.West,
                new(CachedMaterial, Rot4)[1] { (EdgeFlat, Rot4.East) }
            },
            {
                EdgeDirections.North | EdgeDirections.East,
                new(CachedMaterial, Rot4)[1] { (EdgeCornerOuter, Rot4.West) }
            },
            {
                EdgeDirections.East | EdgeDirections.South,
                new(CachedMaterial, Rot4)[1] { (EdgeCornerOuter, Rot4.North) }
            },
            {
                EdgeDirections.South | EdgeDirections.West,
                new(CachedMaterial, Rot4)[1] { (EdgeCornerOuter, Rot4.East) }
            },
            {
                EdgeDirections.North | EdgeDirections.West,
                new(CachedMaterial, Rot4)[1] { (EdgeCornerOuter, Rot4.South) }
            },
            {
                EdgeDirections.North | EdgeDirections.South,
                new(CachedMaterial, Rot4)[2]
                {
                    (EdgeFlat, Rot4.South),
                    (EdgeFlat, Rot4.North)
                }
            },
            {
                EdgeDirections.East | EdgeDirections.West,
                new(CachedMaterial, Rot4)[2]
                {
                    (EdgeFlat, Rot4.West),
                    (EdgeFlat, Rot4.East)
                }
            },
            {
                EdgeDirections.North | EdgeDirections.East | EdgeDirections.South,
                new(CachedMaterial, Rot4)[1] { (EdgeUShape, Rot4.West) }
            },
            {
                EdgeDirections.East | EdgeDirections.South | EdgeDirections.West,
                new(CachedMaterial, Rot4)[1] { (EdgeUShape, Rot4.North) }
            },
            {
                EdgeDirections.North | EdgeDirections.South | EdgeDirections.West,
                new(CachedMaterial, Rot4)[1] { (EdgeUShape, Rot4.East) }
            },
            {
                EdgeDirections.North | EdgeDirections.East | EdgeDirections.West,
                new(CachedMaterial, Rot4)[1] { (EdgeUShape, Rot4.South) }
            },
            {
                EdgeDirections.North | EdgeDirections.East | EdgeDirections.South | EdgeDirections.West,
                new(CachedMaterial, Rot4)[1] { (EdgeOShape, Rot4.North) }
            }
        };

        public override bool Visible
        {
            get
            {
                // Always visible when terrain debugging is on, regardless of biome
                return DebugViewSettings.drawTerrain;
            }
        }

        public SectionLayer_StationFloor(Section section) : base(section)
        {
            relevantChangeTypes = MapMeshFlagDefOf.Terrain | MapMeshFlagDefOf.Buildings;
        }

        public override void Regenerate()
        {
            ClearSubMeshes(MeshParts.All);

            Map map = base.Map;
            TerrainGrid terrainGrid = map.terrainGrid;
            CellRect cellRect = section.CellRect;
            float edgeAltitude = AltitudeLayer.TerrainScatter.AltitudeFor();
            float cornerAltitude = AltitudeLayer.TerrainScatter.AltitudeFor() + 0.001f; // Slightly higher to prevent Z-fighting

            foreach (IntVec3 cell in cellRect)
            {
                if (ShouldDrawEdgesOn(cell, terrainGrid, out var edgeDirections, out var cornerDirections))
                {
                    DrawEdges(cell, edgeDirections, edgeAltitude);
                    DrawCorners(cell, cornerDirections, edgeDirections, cornerAltitude);
                }
            }

            FinalizeMesh(MeshParts.All);
        }

        private void DrawEdges(IntVec3 cell, EdgeDirections edgeDirections, float altitude)
        {
            if (EdgeMats.TryGetValue(edgeDirections, out var value))
            {
                for (int i = 0; i < value.Length; i++)
                {
                    var (cachedMaterial, rotation) = value[i];
                    AddQuad(GetSubMesh(cachedMaterial.Material), cell, altitude, rotation);
                }
            }
        }

        private void DrawCorners(IntVec3 cell, CornerDirections cornerDirections, EdgeDirections edgeDirections, float altitude)
        {
            // Fixed rotations for inner corners
            if (cornerDirections.HasFlag(CornerDirections.NorthWest) && !edgeDirections.HasFlag(EdgeDirections.North) && !edgeDirections.HasFlag(EdgeDirections.West))
            {
                AddQuad(GetSubMesh(EdgeCornerInner.Material), cell, altitude, Rot4.East); // Fixed: was Rot4.South
            }
            if (cornerDirections.HasFlag(CornerDirections.NorthEast) && !edgeDirections.HasFlag(EdgeDirections.North) && !edgeDirections.HasFlag(EdgeDirections.East))
            {
                AddQuad(GetSubMesh(EdgeCornerInner.Material), cell, altitude, Rot4.South); // Fixed: was Rot4.West
            }
            if (cornerDirections.HasFlag(CornerDirections.SouthEast) && !edgeDirections.HasFlag(EdgeDirections.South) && !edgeDirections.HasFlag(EdgeDirections.East))
            {
                AddQuad(GetSubMesh(EdgeCornerInner.Material), cell, altitude, Rot4.West); // Fixed: was Rot4.North
            }
            if (cornerDirections.HasFlag(CornerDirections.SouthWest) && !edgeDirections.HasFlag(EdgeDirections.South) && !edgeDirections.HasFlag(EdgeDirections.West))
            {
                AddQuad(GetSubMesh(EdgeCornerInner.Material), cell, altitude, Rot4.North); // Fixed: was Rot4.East
            }
        }

        private void AddQuad(LayerSubMesh sm, IntVec3 cell, float altitude, Rot4 rotation)
        {
            int count = sm.verts.Count;
            int rotOffset = Mathf.Abs(4 - rotation.AsInt);

            for (int i = 0; i < 4; i++)
            {
                sm.verts.Add(new Vector3((float)cell.x + UVs[i].x, altitude, (float)cell.z + UVs[i].y));
                sm.uvs.Add(UVs[(rotOffset + i) % 4]);
            }

            sm.tris.Add(count);
            sm.tris.Add(count + 1);
            sm.tris.Add(count + 2);
            sm.tris.Add(count);
            sm.tris.Add(count + 2);
            sm.tris.Add(count + 3);
        }

        private bool ShouldDrawEdgesOn(IntVec3 cell, TerrainGrid terrainGrid, out EdgeDirections edgeDirections, out CornerDirections cornerDirections)
        {
            edgeDirections = EdgeDirections.None;
            cornerDirections = CornerDirections.None;

            // Check if this cell has StationFloor
            TerrainDef terrainDef = terrainGrid.TerrainAt(cell);
            if (terrainDef == null || terrainDef.defName != "StationFloor")
                return false;

            // Check cardinal directions for edges
            for (int i = 0; i < GenAdj.CardinalDirections.Length; i++)
            {
                IntVec3 adjacentCell = cell + GenAdj.CardinalDirections[i];
                if (!adjacentCell.InBounds(base.Map))
                {
                    edgeDirections |= (EdgeDirections)(1 << i);
                    continue;
                }

                TerrainDef adjacentTerrain = terrainGrid.TerrainAt(adjacentCell);
                if (adjacentTerrain == null || adjacentTerrain.defName != "StationFloor")
                {
                    edgeDirections |= (EdgeDirections)(1 << i);
                }
            }

            // Check diagonal directions for corners
            for (int j = 0; j < GenAdj.DiagonalDirections.Length; j++)
            {
                IntVec3 diagonalCell = cell + GenAdj.DiagonalDirections[j];
                if (!diagonalCell.InBounds(base.Map))
                {
                    cornerDirections |= (CornerDirections)(1 << j);
                    continue;
                }

                TerrainDef diagonalTerrain = terrainGrid.TerrainAt(diagonalCell);
                if (diagonalTerrain == null || diagonalTerrain.defName != "StationFloor")
                {
                    cornerDirections |= (CornerDirections)(1 << j);
                }
            }

            // Return true if we need to draw edges or corners
            return edgeDirections != EdgeDirections.None || cornerDirections != CornerDirections.None;
        }
    }
}