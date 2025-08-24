using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace DecompressionMod
{
    public class PlaceWorker_MiniAirlock : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            // Get the required wall positions based on rotation
            IntVec3[] requiredWallPositions = GetRequiredWallPositions(loc, rot);

            foreach (IntVec3 wallPos in requiredWallPositions)
            {
                if (!wallPos.InBounds(map))
                {
                    return new AcceptanceReport("Must be placed within map bounds");
                }

                if (!HasWallAt(wallPos, map))
                {
                    return new AcceptanceReport("Requires walls on both sides");
                }
            }

            return AcceptanceReport.WasAccepted;
        }

        public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing = null)
        {
            // Draw the airlock ghost
            base.DrawGhost(def, center, rot, ghostCol, thing);

            // Draw wall requirement indicators
            IntVec3[] requiredWallPositions = GetRequiredWallPositions(center, rot);

            foreach (IntVec3 wallPos in requiredWallPositions)
            {
                if (!wallPos.InBounds(Find.CurrentMap)) continue;

                Color wallIndicatorColor = HasWallAt(wallPos, Find.CurrentMap) ? Color.green : Color.red;
                wallIndicatorColor.a = 0.6f;

                // Draw a small indicator showing where walls are required
                GenDraw.DrawFieldEdges(new IntVec3[] { wallPos }.ToList(), wallIndicatorColor);
            }
        }

        private IntVec3[] GetRequiredWallPositions(IntVec3 center, Rot4 rotation)
        {
            // Airlock needs walls on both sides perpendicular to its facing direction
            IntVec3 leftWall, rightWall;

            switch (rotation.AsInt)
            {
                case 0: // North (facing north, walls on east/west)
                    leftWall = center + IntVec3.West;
                    rightWall = center + IntVec3.East;
                    break;
                case 1: // East (facing east, walls on north/south)
                    leftWall = center + IntVec3.North;
                    rightWall = center + IntVec3.South;
                    break;
                case 2: // South (facing south, walls on east/west)
                    leftWall = center + IntVec3.West;
                    rightWall = center + IntVec3.East;
                    break;
                case 3: // West (facing west, walls on north/south)
                    leftWall = center + IntVec3.North;
                    rightWall = center + IntVec3.South;
                    break;
                default:
                    leftWall = center + IntVec3.West;
                    rightWall = center + IntVec3.East;
                    break;
            }

            return new IntVec3[] { leftWall, rightWall };
        }

        private bool HasWallAt(IntVec3 position, Map map)
        {
            if (!position.InBounds(map)) return false;

            // Check for any wall-like edifice
            Building edifice = position.GetEdifice(map);
            if (edifice == null) return false;

            // Must be a wall or other solid structure
            return edifice.def.IsWall ||
                   edifice.def.Fillage == FillCategory.Full ||
                   (edifice.def.building?.isEdifice == true && edifice.def.passability == Traversability.Impassable);
        }
    }
}