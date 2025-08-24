using RimWorld;
using System.Linq;
using UnityEngine;
using Verse;

namespace DecompressionMod
{
    public static class Decompression_Utility
    {
        public static IntVec3 FindSimpleBreachPoint(Room room, Map map)
        {
            Log.Message($"[Decompression] Looking for breach point in room with {room.Cells.Count()} cells");

            foreach (var door in map.listerBuildings.AllBuildingsColonistOfClass<Building_Door>())
            {
                if (door.Open && door.Spawned)
                {
                    bool connectsToOurRoom = false;
                    float maxAdjacentVacuum = 0f;

                    foreach (var adjCell in GenAdj.CardinalDirections.Select(dir => door.Position + dir))
                    {
                        if (adjCell.InBounds(map))
                        {
                            var adjRoom = adjCell.GetRoom(map);
                            if (adjRoom == room)
                            {
                                connectsToOurRoom = true;
                            }
                            if (adjRoom != null)
                            {
                                maxAdjacentVacuum = Mathf.Max(maxAdjacentVacuum, adjRoom.Vacuum);
                            }
                        }
                    }

                    if (connectsToOurRoom && maxAdjacentVacuum > 0.8f)
                    {
                        Log.Message($"[Decompression] Found door breach at {door.Position}");
                        return door.Position;
                    }
                }
            }

            // Fallback
            var centerCell = room.Cells.FirstOrDefault();
            Log.Message($"[Decompression] Using room center as breach point: {centerCell}");
            return centerCell;
        }
    }
}