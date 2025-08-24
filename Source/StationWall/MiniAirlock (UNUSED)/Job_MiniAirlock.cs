using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace DecompressionMod
{
    [DefOf]
    public static class DecompressionJobDefOf
    {
        public static JobDef UseAirlock;

        static DecompressionJobDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(DecompressionJobDefOf));
        }
    }

    // FIXED: Simplified job driver that doesn't cause AI freezing
    public class JobDriver_UseAirlock : JobDriver
    {
        private Building_MiniAirlock Airlock => (Building_MiniAirlock)TargetThingA;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // Don't reserve the airlock - multiple pawns might want to use it
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // Fail if airlock is destroyed or inaccessible
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => !Airlock.PawnCanOpen(pawn));

            // FIXED: Move to adjacent cell instead of standing on airlock
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            // Handle the airlock operation
            yield return new Toil
            {
                initAction = delegate
                {
                    switch (job.count)
                    {
                        case 1:
                            Airlock.ForceInnerDoor(!Airlock.InnerDoorOpen, pawn);
                            break;
                        case 2:
                            Airlock.ForceOuterDoor(!Airlock.OuterDoorOpen, pawn);
                            break;
                        default:
                            Airlock.ManualCycle(pawn);
                            break;
                    }
                    Messages.Message($"{pawn.LabelShort} operated the airlock.", Airlock, MessageTypeDefOf.TaskCompletion);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };

        }
    }
}