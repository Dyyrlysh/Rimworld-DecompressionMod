using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace StationWall.ReinforcedWindow
{
    public class JoyGiver_ViewReinforcedWindow : JoyGiver
    {
        private static readonly List<Thing> candidates = new List<Thing>();

        public override Job TryGiveJob(Pawn pawn)
        {
            try
            {
                GetSearchSet(pawn, candidates);

                // Filter to only those the pawn can reserve
                var valid = candidates.Where(t => pawn.CanReserve(t, 1, -1, null, false));

                if (!valid.TryRandomElement(out Thing chosenWindow))
                    return null;

                return JobMaker.MakeJob(def.jobDef, chosenWindow);
            }
            finally
            {
                candidates.Clear();
            }
        }
    }
}