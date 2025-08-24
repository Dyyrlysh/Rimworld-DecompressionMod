using System.Collections.Generic;
using Verse;

namespace DecompressionMod
{
    public class DecompressionEvent
    {
        public Room sourceRoom;
        public float pressureDelta;
        public DecompressionSeverity severity;
        public int ticksRemaining;
        public List<IntVec3> affectedCells;
        public IntVec3 breachPoint;
    }

    public enum DecompressionSeverity
    {
        Minor,
        Major
    }
}