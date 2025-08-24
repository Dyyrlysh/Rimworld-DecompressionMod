using Verse;
using RimWorld;

namespace DecompressionMod
{
    internal static class MiniAirlockRegionHelper
    {
        public static void GetEdgeCells(Building_MiniAirlock a, out IntVec3 edgeA, out IntVec3 edgeB)
        {
            switch (a.Rotation.AsInt)
            {
                case 0: // North
                case 2: // South
                    edgeA = a.Position + IntVec3.West;
                    edgeB = a.Position + IntVec3.East;
                    break;
                case 1: // East
                case 3: // West
                    edgeA = a.Position + IntVec3.North;
                    edgeB = a.Position + IntVec3.South;
                    break;
                default:
                    edgeA = a.Position + IntVec3.West;
                    edgeB = a.Position + IntVec3.East;
                    break;
            }
        }

        public static bool TryGetAirlockForEdgeCell(Map map, IntVec3 cell, out Building_MiniAirlock airlock)
        {
            foreach (var dir in GenAdj.CardinalDirections)
            {
                var center = cell + dir;
                if (!center.InBounds(map)) continue;

                var edifice = center.GetEdifice(map) as Building_MiniAirlock;
                if (edifice == null || !edifice.Spawned) continue;

                GetEdgeCells(edifice, out var e1, out var e2);
                if (cell == e1 || cell == e2)
                {
                    airlock = edifice;
                    return true;
                }
            }

            airlock = null;
            return false;
        }
    }
}