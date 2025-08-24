using HarmonyLib;
using RimWorld;
using System;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using System.Collections.Generic;
using System.Security.Permissions;

namespace DecompressionMod
{
    [DefOf]
    public static class OxygenDefOf
    {
        public static HediffDef Asphyxiation;
        public static HediffDef HypoxicComa;
        public static JobDef SeekOxygen;
        public static ThoughtDef Suffocating;
        public static MentalStateDef VacuumPanic;
        public static NeedDef Oxygen;

        static OxygenDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(OxygenDefOf));
        }
    }

    public enum OxygenUrgency
    {
        None,
        Low,        // 20% - medium priority
        Critical,   // 10% - override priority
        Suffocating // 0% - EMERGENCY!
    }

    public class Need_Oxygen : Need
    {
        private CompOxygenTracker OxygenComp => pawn.GetComp<CompOxygenTracker>();

        public Need_Oxygen(Pawn pawn) : base(pawn)
        {
            // Threshold markers
            threshPercents = new List<float> { 0.2f, 0.1f, 0f };
        }

        public override float MaxLevel => OxygenComp?.maxOxygen ?? 1800f;

        public override float CurLevel
        {
            get => OxygenComp?.currentOxygen ?? MaxLevel;
            set { } // Oxygen comp controls this.
        }

        public override bool ShowOnNeedList
        {
            get
            {
                var comp = OxygenComp;
                if (comp == null) return false;

                // Show if in vacuum OR recovering (not at full oxygen)
                return comp.inVacuum || comp.currentOxygen < comp.maxOxygen;
            }
        }

        public override int GUIChangeArrow
        {
            get
            {
                var comp = OxygenComp;
                if (comp == null) return 0;

                if (comp.inVacuum)
                    return -1; // Depleting
                else if (comp.currentOxygen < comp.maxOxygen)
                    return 1; // Replenishing
                else
                    return 0; // Stable
            }
        }

        public override void NeedInterval()
        {
            // CompOxygenTracker handles this.
        }

        public override void SetInitialLevel()
        {
            // Ditto.
        }

        public override string GetTipString()
        {
            var comp = OxygenComp;
            if (comp == null)
                return "No oxygen tracking available.";

            /* string status = comp.inVacuum ? "In vacuum - depleting" : "Breathable atmosphere";
            if (!comp.inVacuum && comp.currentOxygen < comp.maxOxygen)
                status += "Replenishing oxygen"; */

            string baseText = (LabelCap + ": " + CurLevelPercentage.ToStringPercent().Colorize(ColoredText.TipSectionTitleColor) +
                "\n" +
                def.description);

            if (comp.theoreticalMaxOxygen > comp.maxOxygen)
            {
                float breathingCapacity = pawn.health.capacities.GetLevel(PawnCapacityDefOf.Breathing);
                baseText += $"\n\nBreathing capacity: {breathingCapacity:P0}";
                baseText += $"\nApparelcapacity: {comp.theoreticalMaxOxygen:F0}";
            }

            return baseText;
        }
    }

    public class CompOxygenTracker : ThingComp
    {
        public float currentOxygen;
        public float maxOxygen;
        public bool inVacuum;
        public int lastVacuumTick;
        public float theoreticalMaxOxygen;
        private int lastOxygenJobTick = -999;
        private const int OxygenJobCooldownTicks = 60; // 1 second cooldown for seeking oxygen jobs

        public CompProperties_OxygenTracker Props => (CompProperties_OxygenTracker)props;

        private const float BaseOxygenDepletionRate = 1f; // Base rate of oxygen depletion per tick in vacuum
        private const float BaseOxygenCapacity = 1800f; // Base oxygen for naked pawn (30 seconds = 1800 ticks)

        public override void CompTick()
        {
            if (parent is Pawn pawn && pawn.Spawned && !pawn.Dead)
            {
                CheckVacuumStatus(pawn);
                if (inVacuum)
                {
                    DepleteOxygen(pawn);

                    var urgency = GetOxygenUrgency();
                    /* if ((urgency == OxygenUrgency.Suffocating || 
                        (urgency == OxygenUrgency.Critical && 
                        !pawn.Drafted)) &&
                         pawn.jobs.IsCurrentJobPlayerInterruptible() &&
                         !pawn.Downed &&
                          Find.TickManager.TicksGame - lastOxygenJobTick > OxygenJobCooldownTicks &&
                          pawn.CurJobDef != OxygenDefOf.SeekOxygen) // Don't interrupt existing oxygen job
                    {
                        IntVec3 safeSpot = FindNearestOxygenatedArea(pawn);
                        if (safeSpot.IsValid)
                        {
                            Job oxygenJob = JobMaker.MakeJob(OxygenDefOf.SeekOxygen, safeSpot);
                            oxygenJob.locomotionUrgency = urgency == OxygenUrgency.Suffocating ?
                                LocomotionUrgency.Sprint : LocomotionUrgency.Jog;

                            pawn.jobs.StartJob(oxygenJob, JobCondition.InterruptForced, null,
                                resumeCurJobAfterwards: false, cancelBusyStances: true, null,
                                JobTag.SatisfyingNeeds, fromQueue: false, canReturnCurJobToPool: false);

                            lastOxygenJobTick = Find.TickManager.TicksGame;
                        }
                    } */

                    if (urgency == OxygenUrgency.Suffocating &&
                        !pawn.InMentalState &&
                        Find.TickManager.TicksGame - lastOxygenJobTick > OxygenJobCooldownTicks)
                    {
                        pawn.mindState.mentalStateHandler.TryStartMentalState(OxygenDefOf.VacuumPanic,
                            "Out of air", forced: true, forceWake: true);
                        lastOxygenJobTick = Find.TickManager.TicksGame;
                    }
                }
            }
        }

        private void CheckVacuumStatus(Pawn pawn)
        {
            float vacuum = pawn.Position.GetVacuum(pawn.Map);
            bool newInVacuum = vacuum > 0.8f; // High vacuum threshold

            if (newInVacuum != inVacuum)
            {
                inVacuum = newInVacuum;
                if (inVacuum)
                {
                    // Only calculate capacity if this is truly the first time
                    if (maxOxygen <= 0)
                    {
                        CalculateOxygenCapacity(pawn);
                    }
                    lastVacuumTick = Find.TickManager.TicksGame;
                    Log.Message($"[OXYGEN] {pawn.Name.ToStringShort} entered vacuum. Oxygen: {currentOxygen:F0}/{maxOxygen:F0}.");
                }
            }

            if (!inVacuum && currentOxygen < maxOxygen)
            {
                var panicState = pawn.mindState.mentalStateHandler.CurState as MentalState_VacuumPanic;
                if (panicState != null)
                {
                    panicState.RecoverFromState();
                }

                float recoveryRate = CalculateOxygenRecoveryRate(vacuum, pawn);
                currentOxygen = Math.Min(maxOxygen, currentOxygen + recoveryRate);

                if (currentOxygen >= maxOxygen)
                {
                    RemoveAsphyxiationHediffs(pawn);
                }
            }
        }

        private float CalculateOxygenRecoveryRate(float vacuum, Pawn pawn)
        {
            // Base recovery rate as PERCENTAGE per tick
            float baseRecoveryPercent = 0.01f; // 1% per tick = ~1.7 seconds to full

            // Better atmosphere = faster recovery
            float atmosphereQuality = 1f - vacuum;
            atmosphereQuality = Mathf.Clamp01(atmosphereQuality);
            float atmosphereMultiplier = Mathf.Max(0.1f, atmosphereQuality);

            // Only breathing capacity affects recovery
            float breathingCapacity = pawn.health.capacities.GetLevel(PawnCapacityDefOf.Breathing);

            // Return absolute amount based on their max capacity
            float finalRate = maxOxygen * baseRecoveryPercent * atmosphereMultiplier * breathingCapacity;

            return finalRate;
        }

        private void DepleteOxygen(Pawn pawn)
        {
            if (currentOxygen > 0)
            {
                currentOxygen -= BaseOxygenDepletionRate;
                if (currentOxygen <= 0)
                {
                    currentOxygen = 0;
                    ApplyAsphyxiation(pawn);
                }
            }
        }

        public void CalculateOxygenCapacity(Pawn pawn)
        {
            float headgearMultiplier = GetHeadgearOxygenMultiplier(pawn);
            float torsoMultiplier = GetTorsoOxygenMultiplier(pawn);
            float breathingCapacity = pawn.health.capacities.GetLevel(PawnCapacityDefOf.Breathing);

            // Calculate new capacity
            float newTheoreticalMax = BaseOxygenCapacity * headgearMultiplier * torsoMultiplier;
            float newMaxOxygen = newTheoreticalMax * breathingCapacity;

            // PRESERVE oxygen percentage, not absolute amount
            float currentPercentage = (maxOxygen > 0) ? (currentOxygen / maxOxygen) : 1f;

            // Update capacity values
            theoreticalMaxOxygen = newTheoreticalMax;
            maxOxygen = newMaxOxygen;

            // Apply the same percentage to new capacity
            currentOxygen = maxOxygen * currentPercentage;

            // Clamp to valid range
            currentOxygen = Mathf.Clamp(currentOxygen, 0f, maxOxygen);

            Log.Message($"[OXYGEN] {pawn.Name.ToStringShort} recalculated capacity: {currentOxygen:F0}/{maxOxygen:F0} ({currentPercentage:P0}) - Head: {headgearMultiplier:F2}x, Torso: {torsoMultiplier:F2}x");
        }

        private float GetHeadgearOxygenMultiplier(Pawn pawn)
        {
            if (pawn.apparel?.WornApparel == null) return 1f;

            var headgear = pawn.apparel.WornApparel.FirstOrDefault(a =>
                a.def.apparel.bodyPartGroups.Any(bpg =>
                bpg.defName == "FullHead" || bpg.defName == "Eyes"));

            if (headgear == null) return 1f;

            string headName = headgear.def.defName.ToLower();

            // Full vacsuit helmet - best
            if (headName.Contains("vacsuit") || headName.Contains("spacesuit"))
                return 24f; // ~6 minutes alone

            // High-tech helmets - good 
            if (headName.Contains("recon") || headName.Contains("marine") ||
                headName.Contains("cataphract") || headName.Contains("prestige"))
                return 8f; // ~2 minutes alone

            return 1f;
        }

        private float GetTorsoOxygenMultiplier(Pawn pawn)
        {
            if (pawn.apparel?.WornApparel == null) return 1f;

            var torsoWear = pawn.apparel.WornApparel.FirstOrDefault(a =>
            a.def.apparel.bodyPartGroups.Any(bpg => bpg.defName == "Torso"));
            if (torsoWear == null) return 1f;

            string torsoName = torsoWear.def.defName.ToLower();

            // BEST oxygen
            if (torsoName.Contains("vacsuit") || torsoName.Contains("spacesuit"))
                return 12f; // Combined with helmet = ~2+ hours

            // GOOD oxygen
            if (torsoName.Contains("recon") || torsoName.Contains("marine") ||
                torsoName.Contains("cataphract") || torsoName.Contains("prestige"))
                return 6f; // Combined with helmet = ~1+ hour?

            return 1f; // Default, no benefit
        }

        private void ApplyAsphyxiation(Pawn pawn)
        {
            var existingAsphyxiation = pawn.health.hediffSet.GetFirstHediffOfDef(OxygenDefOf.Asphyxiation);
            if (existingAsphyxiation == null)
            {
                var hediff = HediffMaker.MakeHediff(OxygenDefOf.Asphyxiation, pawn);
                hediff.Severity = 0f; // Explicitly start at 0%
                pawn.health.AddHediff(hediff);
                Log.Message($"[OXYGEN] {pawn.Name.ToStringShort} began asphyxiating!");
            }
        }

        private void RemoveAsphyxiationHediffs(Pawn pawn)
        {
            var asphyxiation = pawn.health.hediffSet.GetFirstHediffOfDef(OxygenDefOf.Asphyxiation);
            if (asphyxiation != null)
            {
                pawn.health.RemoveHediff(asphyxiation);
            }
        }

        public override void DrawGUIOverlay()
        {
            if (parent is Pawn pawn && pawn.Spawned && !pawn.Dead && Find.CameraDriver.CurrentZoom <= CameraZoomRange.Middle)
            {
                if (inVacuum || (!inVacuum && currentOxygen < maxOxygen))
                {
                    DrawOxygenBar(pawn);
                }
            }
        }

        private void DrawOxygenBar(Pawn pawn)
        {
            Vector2 pos = GenMapUI.LabelDrawPosFor(pawn, -0.6f);
            pos.y += 20f;

            float breathingCapacity = pawn.health.capacities.GetLevel(PawnCapacityDefOf.Breathing);
            float fillPercent = maxOxygen > 0 ? (currentOxygen / maxOxygen) : 0f;
            bool criticalOxygen = fillPercent <= 0.1f; // Critical if 10% or less

            Rect barRect = new Rect(pos.x - 28f, pos.y, 56f, 12f);
            Rect innerRect = new Rect(barRect.x + 2f, barRect.y + 2f, barRect.width - 4f, barRect.height - 4f);

            // Use vanilla progress bar colors (from MoteProgressBar)
            Color unfilledColor = new Color(0.3f, 0.3f, 0.3f, 0.65f); // Vanilla unfilled
            // Color blockedColor = SolidColorMaterials.NewSolidColorTexture(new Color(0.65f, 0.65f, 0.65f, 0.75f));
            Color usableColor = new Color(0.4f, 0.4f, 0.4f, 0.65f);
            Color fillColor = fillPercent > 0.2f ?
                new Color(0.4f, 0.9f, 0.9f, 0.65f) : // Turquoise with vanilla alpha
                new Color(0.9f, 0.2f, 0.2f, 1f);   // Red with vanilla alpha

            if (criticalOxygen)
            {
                Rect glowRect = new Rect(barRect.x - 2f, barRect.y - 2f, barRect.width + 4f, barRect.height + 4f);
                Color glowColor = new Color(1f, 0.2f, 0.2f, 0.3f * Mathf.Sin(Time.time * 3f) + 0.2f); // Red glow
                Widgets.DrawBoxSolid(glowRect, glowColor);
            }

            // Draw background (unfilled)
            Widgets.DrawBoxSolid(barRect, unfilledColor);

            float usableWidth = innerRect.width * breathingCapacity;
            if (usableWidth > 0)
            {
                Rect usableRect = new Rect(innerRect.x, innerRect.y, usableWidth, innerRect.height);
                Widgets.DrawBoxSolid(usableRect, usableColor);

                // Draw current oxygen fill within usable area
                if (fillPercent > 0)
                {
                    Rect fillRect = new Rect(innerRect.x, innerRect.y, usableWidth * fillPercent, innerRect.height);
                    Widgets.DrawBoxSolid(fillRect, fillColor);
                }
            }

            if (breathingCapacity < 1f)
            {
                float blockedWidth = innerRect.width * (1f - breathingCapacity);
                Rect blockedRect = new Rect(innerRect.x + usableWidth, innerRect.y +0.25f, blockedWidth, innerRect.height);

                // Draw a subtle pattern or outline to show this area is blocked
                Widgets.DrawBox(blockedRect, 1, BaseContent.GreyTex);
            }

            if (criticalOxygen)
            {
                Color redBorderColor = new Color(1f, 0.2f, 0.2f, 0.8f); // Bright red border
                UnityEngine.Texture2D redTexture = SolidColorMaterials.NewSolidColorTexture(redBorderColor);
                Widgets.DrawBox(barRect, 1, redTexture);
            }

            // Draw fill
            /* if (fillPercent > 0)
            {
                // Add margin like vanilla (0.12f margin on a scale, approximately 1 pixel on our 12px bar)
                Rect fillRect = new Rect(barRect.x + 2f, barRect.y + 2f,
                                        (barRect.width - 4f) * fillPercent, barRect.height - 4f);
                Widgets.DrawBoxSolid(fillRect, fillColor);
            } */

            // No additional border - vanilla progress bars don't have one
        }

        /* private void DrawOxygenMeter(Pawn pawn)
        {
            if (!pawn.Spawned || pawn.Map != Find.CurrentMap) return;

            Vector3 drawPos = pawn.DrawPos + Vector3.up * 0.1f;
            Vector2 screenPos = Find.Camera.WorldToScreenPoint(drawPos);

            // Convert to GUI coordinates (flip Y)
            screenPos.y = Screen.height - screenPos.y;
            screenPos /= Prefs.UIScale;

            // Vanilla meter dimensions - match work progress bars
            float meterWidth = 32f;
            float meterHeight = 8f;
            float offsetX = -meterWidth / 2f; // Center horizontally
            float offsetY = -60f; // Above pawn

            Rect meterRect = new Rect(
                screenPos.x + offsetX,
                screenPos.y + offsetY,
                meterWidth,
                meterHeight
            );

            float fillPercent = maxOxygen > 0 ? (currentOxygen / maxOxygen) : 0f;

            // Background (dark gray like vanilla)
            Widgets.DrawBoxSolid(meterRect, new Color(0.05f, 0.05f, 0.05f, 0.8f));

            // Fill color - turquoise until 20%, then red
            Color fillColor = fillPercent > 0.2f ?
                new Color(0.2f, 0.8f, 0.8f) : // Turquoise
                new Color(0.8f, 0.2f, 0.2f);  // Red

            // Fill rect
            Rect fillRect = new Rect(
                meterRect.x + 1f,
                meterRect.y + 1f,
                (meterRect.width - 2f) * fillPercent,
                meterRect.height - 2f
            );

            if (fillPercent > 0)
            {
                Widgets.DrawBoxSolid(fillRect, fillColor);
            }

            // Border (light gray like vanilla)
            Widgets.DrawBox(meterRect, 1, new Color(0.7f, 0.7f, 0.7f));
        } */

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref currentOxygen, "currentOxygen", 0f);
            Scribe_Values.Look(ref maxOxygen, "maxOxygen", BaseOxygenCapacity);
            Scribe_Values.Look(ref inVacuum, "inVacuum", false);
            Scribe_Values.Look(ref lastVacuumTick, "lastVacuumTick", -1);
            Scribe_Values.Look(ref theoreticalMaxOxygen, "theoreticalMaxOxygen", BaseOxygenCapacity);
        }

        public IntVec3 FindNearestOxygenatedArea(Pawn pawn)
        {
            Map map = pawn.Map;
            int searchRadius = 30;

            var safeRooms = map.regionGrid.AllRooms
                .Where(room => room.Cells.Any() && room.Vacuum < 0.2f) // Safe atmosphere
                .OrderBy(room => room.Cells.First().DistanceTo(pawn.Position))
                .Take(10); // Only check 10 closest rooms

            // Look for safe rooms.
            foreach (var room in safeRooms)
            {
                var accessibleCell = room.Cells
                    .Where(cell => cell.InHorDistOf(pawn.Position, searchRadius) &&
                                   cell.Standable(map) &&
                                   !cell.IsForbidden(pawn) &&
                                   !cell.Fogged(map))
                    .OrderBy(cell => cell.DistanceTo(pawn.Position))
                    .FirstOrDefault();

                if (accessibleCell != default(IntVec3) &&
                    pawn.CanReach(accessibleCell, PathEndMode.OnCell, Danger.Some))
                {
                    return accessibleCell; // Found a safe cell
                }
            }

            return IntVec3.Invalid;
        }

        public OxygenUrgency GetOxygenUrgency()
        {
            if (currentOxygen <= 0)
                return OxygenUrgency.Suffocating;
            if (maxOxygen > 0)
            {
                float percent = currentOxygen / maxOxygen;
                if (percent <= 0.1f)
                    return OxygenUrgency.Critical; // 10% or less
                if (percent <= 0.2f)
                    return OxygenUrgency.Low; // 20% or less
            }
            return OxygenUrgency.None; // Normal or above
        }

        public bool ShouldSeekOxygen()
        {
            if (!inVacuum) return false;

            var urgency = GetOxygenUrgency();
            return 
                urgency == OxygenUrgency.Low || 
                urgency == OxygenUrgency.Critical || 
                urgency == OxygenUrgency.Suffocating;
        }

        public bool ShouldInterruptCurrentJob()
        {
            var urgency = GetOxygenUrgency();
            return 
                urgency == OxygenUrgency.Critical || 
                urgency == OxygenUrgency.Suffocating;
        }
    }

    public class Alert_LowOxygen : Alert
    {
        private List<Pawn> lowOxygenPawnsResult = new List<Pawn>();

        public Alert_LowOxygen()
        {
            defaultLabel = "Low oxygen";
            defaultPriority = AlertPriority.High;
        }

        private List<Pawn> LowOxygenPawns
        {
            get
            {
                lowOxygenPawnsResult.Clear();
                foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists)
                {
                    var comp = pawn.GetComp<CompOxygenTracker>();
                    if (comp?.inVacuum == true && comp.maxOxygen > 0 &&
                        comp.currentOxygen > 0 &&
                        (comp.currentOxygen / comp.maxOxygen) <= 0.2f)
                    {
                        lowOxygenPawnsResult.Add(pawn);
                    }
                }
                return lowOxygenPawnsResult;
            }
        }

        public override string GetLabel()
        {
            int count = LowOxygenPawns.Count;
            if (count == 0)
                return "";

            if (count == 1)
                return defaultLabel; // Just "Low oxygen"

            return defaultLabel + " x" + count.ToString(); // "Low oxygen x"
        }

        public override TaggedString GetExplanation()
        {
            return "One or more of your colonists is dangerously low on oxygen. They will begin to seek breathable atmosphere to replenish themselves.\n\n" +
                   LowOxygenPawns.Select(p => " " + p.LabelShortCap).ToCommaList(useAnd: true);
        }

        public override AlertReport GetReport()
        {
            List<Pawn> pawns = LowOxygenPawns;
            if (pawns.Count > 0)
            {
                return AlertReport.CulpritsAre(pawns);
            }
            return false;
        }
    }

    public class Alert_Suffocating : Alert_Critical
    {
        private List<Pawn> suffocatingPawnsResult = new List<Pawn>();
        private HashSet<Pawn> previousSuffocatingPawns = new HashSet<Pawn>(); // Track previous pawns

        public Alert_Suffocating()
        {
            defaultLabel = "Suffocation";
        }

        private List<Pawn> SuffocatingPawns
        {
            get
            {
                suffocatingPawnsResult.Clear();
                foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists)
                {
                    var comp = pawn.GetComp<CompOxygenTracker>();
                    if (comp?.inVacuum == true && comp.currentOxygen <= 0)
                    {
                        suffocatingPawnsResult.Add(pawn);
                    }
                }
                return suffocatingPawnsResult;
            }
        }

        public override string GetLabel()
        {
            int count = SuffocatingPawns.Count;
            if (count == 0)
                return "";

            if (count == 1)
                return defaultLabel; // Just "Suffocating"

            return defaultLabel + " x" + count.ToString(); // "Suffocating x"
        }

        public override TaggedString GetExplanation()
        {
            return "One or more of your colonists is suffocating! They will fall into a hypoxic coma and die if they are unable to reach oxygen in time.\n\n" +
                   SuffocatingPawns.Select(p => "  " + p.LabelShortCap).ToCommaList(useAnd: true);
        }

        public override AlertReport GetReport()
        {
            List<Pawn> pawns = SuffocatingPawns;

            // Check for new suffocating pawns and force normal speed
            bool hasNewPawns = false;
            foreach (Pawn pawn in pawns)
            {
                if (!previousSuffocatingPawns.Contains(pawn))
                {
                    hasNewPawns = true;
                    break;
                }
            }

            if (hasNewPawns && pawns.Count > 0)
            {
                // Force normal speed for 5 seconds
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                Find.TickManager.slower.SignalForceNormalSpeedShort();
            }

            // Update the tracking set
            previousSuffocatingPawns.Clear();
            foreach (Pawn pawn in pawns)
            {
                previousSuffocatingPawns.Add(pawn);
            }

            if (pawns.Count > 0)
            {
                return AlertReport.CulpritsAre(pawns);
            }
            return false;
        }

    }

    public class MentalState_VacuumPanic : MentalState
    {
        private int lastOxygenSeekTick = -999;
        private int lastViolenceTick = -999;

        public override void MentalStateTick(int delta)
        {
            base.MentalStateTick(delta);

            var oxygencomp = pawn.GetComp<CompOxygenTracker>();

            // Try to find oxygen every few seconds
            if (Find.TickManager.TicksGame - lastOxygenSeekTick > 180) // Every 3 seconds.
            {
                TrySeekOxygen(oxygencomp);
                lastOxygenSeekTick = Find.TickManager.TicksGame;
            }

            // Random panic behaviors
            if (Find.TickManager.TicksGame - lastViolenceTick > 60 && Rand.Chance(0.1f))
            {
                TryPanicBehavior();
                lastViolenceTick = Find.TickManager.TicksGame;
            }
            
        }

        private void TrySeekOxygen(CompOxygenTracker oxygenComp)
        {
            IntVec3 safeSpot = oxygenComp?.FindNearestOxygenatedArea(pawn) ?? IntVec3.Invalid;
            if (safeSpot.IsValid)
            {
                Job oxygenJob = JobMaker.MakeJob(OxygenDefOf.SeekOxygen, safeSpot);
                oxygenJob.locomotionUrgency = LocomotionUrgency.Sprint;
                oxygenJob.canBashDoors = true; // Panic = break doors
                pawn.jobs.StartJob(oxygenJob, JobCondition.InterruptForced);
            }
        }

        private void TryPanicBehavior()
        {
            // Attack nearby pawns (even allies) in panic - with better error handling
            if (Rand.Chance(0.3f))
            {
                try
                {
                    var nearbyPawns = GenRadial.RadialDistinctThingsAround(pawn.Position, pawn.Map, 3f, useCenter: false)
                        .OfType<Pawn>()
                        .Where(p => p != pawn && p.Spawned && !p.Dead && !p.Downed)
                        .FirstOrDefault();

                    if (nearbyPawns != null && pawn.CanReach(nearbyPawns, PathEndMode.Touch, Danger.Deadly))
                    {
                        Job attackJob = JobMaker.MakeJob(JobDefOf.AttackMelee, nearbyPawns);
                        attackJob.maxNumMeleeAttacks = 1;
                        attackJob.expiryInterval = 300; // Job expires after 5 seconds
                        pawn.jobs.StartJob(attackJob, JobCondition.InterruptForced);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[OXYGEN] Error in vacuum panic behavior for {pawn}: {ex.Message}");
                }
            }
        }

        public override bool ForceHostileTo(Thing t)
        {
            return t is Pawn && Rand.Chance(0.2f);
        }

        public override TaggedString GetBeginLetterText()
        {
            return $"{pawn.LabelShort} is desperate for air and will try to find it by any means necessary. They may behave erratically.";
        }
    }

    [HarmonyPatch(typeof(Pawn_ApparelTracker), "Notify_ApparelAdded")]
    public static class Patch_ApparelAdded
    {
        public static void Postfix(Pawn_ApparelTracker __instance, Apparel apparel)
        {
            var oxygenComp = __instance.pawn.GetComp<CompOxygenTracker>();
            if (oxygenComp != null)
            {
                oxygenComp.CalculateOxygenCapacity(__instance.pawn);
                Log.Message($"[OXYGEN] {__instance.pawn.Name.ToStringShort} recalculated oxygen after adding {apparel.Label}.");
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_ApparelTracker), "Notify_ApparelRemoved")]
    public static class Patch_ApparelRemoved
    {
        public static void Postfix(Pawn_ApparelTracker __instance, Apparel apparel)
        {
            var oxygenComp = __instance.pawn.GetComp<CompOxygenTracker>();
            if (oxygenComp != null)
            {
                oxygenComp.CalculateOxygenCapacity(__instance.pawn);
                Log.Message($"[OXYGEN] {__instance.pawn.Name.ToStringShort} recalculated oxygen after removing {apparel.Label}.");
            }
        }
    }

    public class Hediff_Asphyxiation : HediffWithComps
    {
        private const float MaxSeverityBeforeComa = 1f;
        private const int TicksToMaxSeverity = 60 * 25; // 25 seconds at 0% oxygen

        public override void Tick()
        {
            base.Tick();

            if (pawn?.Dead != false) return;

            var oxygenComp = pawn.GetComp<CompOxygenTracker>();
            if (oxygenComp?.inVacuum != true)
            {
                pawn.health.RemoveHediff(this);
                return;
            }

            // Only progress severity when oxygen is actually at 0%
            if (oxygenComp.currentOxygen <= 0)
            {
                Severity += MaxSeverityBeforeComa / TicksToMaxSeverity;

                if (Severity >= MaxSeverityBeforeComa)
                {
                    if (!pawn.Dead && pawn.health.hediffSet.GetFirstHediffOfDef(OxygenDefOf.HypoxicComa) == null)
                    {
                        pawn.health.RemoveHediff(this);
                        var coma = HediffMaker.MakeHediff(OxygenDefOf.HypoxicComa, pawn);
                        coma.Severity = 0f; // Start coma at 0%
                        pawn.health.AddHediff(coma);
                        Log.Message($"[OXYGEN] {pawn.Name.ToStringShort} fell into hypoxic coma!");
                    }
                }
            }
        }

        public override string LabelInBrackets => $"{(Severity * 100):F0}%";
    }

    public class Hediff_HypoxicComa : HediffWithComps
    {
        private const float MaxSeverityBeforeDeath = 1f;
        private const int TicksToMaxSeverity = 60 * 60 * 3; // 3 hours at 0% oxygen
        private const int RecoveryTicks = 60 * 60; // 1 hour to recover

        public override void Tick()
        {
            base.Tick();

            if (pawn?.Dead != false) return;

            var oxygenComp = pawn.GetComp<CompOxygenTracker>();
            if (oxygenComp?.inVacuum != true)
            {
                // Found oxygen, begin recovery
                Severity -= MaxSeverityBeforeDeath / RecoveryTicks;
                if (Severity <= 0)
                {
                    pawn.health.RemoveHediff(this);
                    Log.Message($"[OXYGEN] {pawn.Name.ToStringShort} recovered from hypoxic coma.");
                }
                return;
            }

            // Only progress to death when oxygen is actually at 0%
            if (oxygenComp.currentOxygen <= 0)
            {
                Severity += MaxSeverityBeforeDeath / TicksToMaxSeverity;

                if (Severity >= MaxSeverityBeforeDeath && !pawn.Dead)
                {
                    pawn.Kill(new DamageInfo(DamageDefOf.Smoke, 9999f));
                    Log.Message($"[OXYGEN] {pawn.Name.ToStringShort} died from asphyxiation.");
                }
            }
        }

        public override string LabelInBrackets => $"{(Severity * 100):F0}%";
        public override bool ShouldRemove => false;
    }

    public class CompProperties_OxygenTracker : CompProperties
        {
            public CompProperties_OxygenTracker()
            {
                compClass = typeof(CompOxygenTracker);
            }
        }

    public class JobGiver_SeekOxygen : ThinkNode_JobGiver
    {
        public override float GetPriority(Pawn pawn)
        {
            var oxygenComp = pawn.GetComp<CompOxygenTracker>();
            if (oxygenComp?.ShouldSeekOxygen() != true)
                return 0f; // No need to seek oxygen

            var urgency = oxygenComp.GetOxygenUrgency();
            return urgency switch
            {
                OxygenUrgency.Suffocating => 11f,      // Higher than food (9.5f)
                OxygenUrgency.Critical => 10f,         // Still higher than food
                OxygenUrgency.Low => 9f,               // Lower than food
                _ => 0f // No need to seek oxygen
            };
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            var oxygenComp = pawn.GetComp<CompOxygenTracker>();
            if (oxygenComp?.ShouldSeekOxygen() != true)
                return null;

            IntVec3 safeSpot = oxygenComp.FindNearestOxygenatedArea(pawn);
            if (safeSpot.IsValid)
            {
                Job job = JobMaker.MakeJob(OxygenDefOf.SeekOxygen, safeSpot);
                job.locomotionUrgency = oxygenComp.GetOxygenUrgency() == OxygenUrgency.Critical ?
                    LocomotionUrgency.Sprint : LocomotionUrgency.Jog;
                return job;
            }
            return null;
        }

    }

    public class JobDriver_SeekOxygen : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true; // No reservations needed for seeking oxygen.
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // Go to safe location.
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);

            // Wait briefly to "catch breath."
            yield return new Toil
            {
                initAction = () =>
                {
                    // Check if we're actually in a safe area.
                    var oxygenComp = pawn.GetComp<CompOxygenTracker>();
                    if (oxygenComp?.inVacuum == false)
                    {
                        // We made it to safety, end the job!
                        ReadyForNextToil();
                    }
                },

                defaultCompleteMode = ToilCompleteMode.Delay,
                defaultDuration = 120 // Wait 2 seconds.
            };
        }
    }

    public class ThoughtWorker_Suffocating : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            var oxygenComp = p.GetComp<CompOxygenTracker>();
            if (oxygenComp?.currentOxygen <= 0 && oxygenComp.inVacuum)
            {
                // Suffocating in vacuum
                return ThoughtState.ActiveAtStage(0);
            }
            return ThoughtState.Inactive; // Not suffocating
        }
    }

}

