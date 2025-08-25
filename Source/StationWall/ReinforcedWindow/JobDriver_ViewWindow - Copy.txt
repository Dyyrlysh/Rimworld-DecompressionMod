using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace StationWall.ReinforcedWindow
{
    public class JobDriver_ViewWindow : JobDriver
    {
        private const int DurationTicks = 400;

        // 10% of full outdoor benefit, applied only on hash intervals (every 15 ticks)
        // This matches how other need systems work and allows proper arrow display
        // Full outdoor benefit is 8.0 * 0.0025 = 0.02 per 150 ticks
        // 10% would be 0.8 * 0.0025 = 0.002 per 150 ticks  
        // Per 15-tick interval: 0.002 / 10 = 0.0002 per hash interval
        private const float OutdoorsBenefitPerHashInterval = 0.0002f;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(TargetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);

            // Go to the window
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.Touch);

            // View the window
            var viewToil = new Toil
            {
                initAction = () =>
                {
                    pawn.rotationTracker.FaceCell(TargetA.Cell);
                },
                tickAction = () =>
                {
                    // Give comfort while viewing (if the API supports it)
                    PawnUtility.GainComfortFromCellIfPossible(pawn, 1, true);

                    // Give joy - meditative is perfect for peaceful window viewing
                    pawn.needs.joy?.GainJoy(0.0004f, JoyKindDefOf.Meditative);

                    // Give outdoors need satisfaction at 10% efficiency, but only on hash intervals
                    // This matches how RimWorld's other need systems work and allows proper arrow display
                    if (pawn.IsHashIntervalTick(15) && pawn.needs?.TryGetNeed<Need_Outdoors>() is Need_Outdoors outdoorsNeed && outdoorsNeed.ShowOnNeedList)
                    {
                        outdoorsNeed.CurLevel = Mathf.Min(outdoorsNeed.CurLevel + OutdoorsBenefitPerHashInterval, 1f);
                    }
                },
                defaultDuration = DurationTicks,
                defaultCompleteMode = ToilCompleteMode.Delay
            };

            yield return viewToil;
        }
    }
}