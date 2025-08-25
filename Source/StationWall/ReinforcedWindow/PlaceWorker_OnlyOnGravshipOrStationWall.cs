using RimWorld;
using Verse;

namespace StationWall.ReinforcedWindow
{
    public class PlaceWorker_OnlyOnGravshipOrStationWall : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(
            BuildableDef def,
            IntVec3 loc,
            Rot4 rot,
            Map map,
            Thing thingToIgnore = null,
            Thing thing = null)
        {
            // Must be placed over Gravship Hull or Station Wall
            var baseEdifice = loc.GetEdifice(map);
            if (baseEdifice == null ||
                (baseEdifice.def.defName != "GravshipHull" && baseEdifice.def.defName != "StationWall"))
            {
                return "Can only be placed over Gravship Hulls or Station Walls.";
            }

            // Check neighbors
            bool hasN = IsValidNeighbor(loc + IntVec3.North, map, def);
            bool hasE = IsValidNeighbor(loc + IntVec3.East, map, def);
            bool hasS = IsValidNeighbor(loc + IntVec3.South, map, def);
            bool hasW = IsValidNeighbor(loc + IntVec3.West, map, def);

            int neighborCount = (hasN ? 1 : 0) + (hasE ? 1 : 0) + (hasS ? 1 : 0) + (hasW ? 1 : 0);

            // Must have exactly two neighbors and they must be opposite
            if (neighborCount != 2)
                return "Reinforced windows must be between two walls or windows.";

            if (!((hasN && hasS) || (hasE && hasW)))
                return "Reinforced windows must be in a straight line.";

            return true;
        }

        private static bool IsValidNeighbor(IntVec3 c, Map map, BuildableDef windowDef)
        {
            if (!c.InBounds(map)) return false;

            var edifice = c.GetEdifice(map);
            if (edifice != null)
            {
                if (edifice.def == windowDef) return true;
                if (edifice.def.defName == "GravshipHull" || edifice.def.defName == "StationWall") return true;
            }

            // Check blueprints/frames
            var things = c.GetThingList(map);
            foreach (var t in things)
            {
                if (t is Blueprint_Build bp && bp.def.entityDefToBuild == windowDef) return true;
                if (t is Frame f && f.def.entityDefToBuild == windowDef) return true;
            }

            return false;
        }
    }
}