using RimWorld;
using System.Linq;
using UnityEngine;
using Verse;

namespace DecompressionMod
{
    public static class Pawn_Injury_Utility
    {
        public static void ApplyAdditionalInjuries(Pawn pawn, float severity, DecompressionSeverity decompressionType)
        {
            if (!pawn.RaceProps.IsFlesh || pawn.Dead) return;

            Log.Message($"[Decompression] Applying severity-based injuries to {pawn.Name.ToStringShort} (severity: {severity:F3})");

            // Determine injury type based on severity ranges
            if (severity < 0.3f) // Minor - Blunt only
            {
                ApplyBluntInjuries(pawn, Rand.Range(1, 3), 0.5f);
            }
            else if (severity < 0.5f) // Moderate - Blunt + Cuts
            {
                ApplyBluntInjuries(pawn, Rand.Range(1, 2), 0.7f);
                ApplyCutInjuries(pawn, Rand.Range(1, 3), 0.6f);
            }
            else if (severity < 0.7f) // Major - Cuts + Organ damage
            {
                ApplyCutInjuries(pawn, Rand.Range(2, 4), 0.8f);
                if (Rand.Chance(0.6f)) ApplyOrganDamage(pawn, 0.7f);
            }
            else // Critical - Cuts + Organ + Frostbite + Body part destruction
            {
                ApplyCutInjuries(pawn, Rand.Range(3, 6), 1.0f);
                if (Rand.Chance(0.8f)) ApplyOrganDamage(pawn, 1.0f);
                if (Rand.Chance(0.4f)) ApplyFrostbite(pawn);
                if (Rand.Chance(0.2f)) ApplyBodyPartDestruction(pawn);
            }
        }

        private static void ApplyBluntInjuries(Pawn pawn, int count, float damageMultiplier)
        {
            for (int i = 0; i < count; i++)
            {
                var targetParts = pawn.health.hediffSet.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Outside)
                    .Where(p => p.coverageAbs > 0)
                    .ToList();

                if (targetParts.Any())
                {
                    var targetPart = targetParts.RandomElementByWeight(p => p.coverageAbs);
                    float damageAmount = Rand.Range(2f, 6f) * damageMultiplier;

                    DamageInfo damageInfo = new DamageInfo(DamageDefOf.Blunt, damageAmount, 0f, -1f, null, targetPart);
                    pawn.TakeDamage(damageInfo);
                }
            }
        }

        private static void ApplyCutInjuries(Pawn pawn, int count, float damageMultiplier)
        {
            for (int i = 0; i < count; i++)
            {
                var targetParts = pawn.health.hediffSet.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Outside)
                    .Where(p => p.coverageAbs > 0)
                    .ToList();

                if (targetParts.Any())
                {
                    var targetPart = targetParts.RandomElementByWeight(p => p.coverageAbs);
                    float damageAmount = Rand.Range(3f, 9f) * damageMultiplier;

                    DamageInfo damageInfo = new DamageInfo(DamageDefOf.Cut, damageAmount, 0f, -1f, null, targetPart);
                    pawn.TakeDamage(damageInfo);
                }
            }
        }

        private static void ApplyOrganDamage(Pawn pawn, float damageMultiplier)
        {
            var internalParts = pawn.health.hediffSet.GetNotMissingParts()
                .Where(p => p.depth == BodyPartDepth.Inside)
                .ToList();

            if (internalParts.Any())
            {
                var targetPart = internalParts.RandomElement();
                float damageAmount = Rand.Range(4f, 12f) * damageMultiplier;

                DamageInfo damageInfo = new DamageInfo(DamageDefOf.Blunt, damageAmount, 0f, -1f, null, targetPart);
                pawn.TakeDamage(damageInfo);
                Log.Message($"[Decompression] Applied organ damage to {targetPart.def.label}");
            }
        }

        private static void ApplyFrostbite(Pawn pawn)
        {
            // Target extremities
            var extremities = pawn.health.hediffSet.GetNotMissingParts()
                .Where(p => p.def.tags?.Any(tag =>
                    tag.defName.ToLower().Contains("finger") ||
                    tag.defName.ToLower().Contains("toe") ||
                    tag.defName.ToLower().Contains("ear") ||
                    tag.defName.ToLower().Contains("nose")) == true)
                .ToList();

            if (!extremities.Any())
            {
                // Fallback to hands/feet
                extremities = pawn.health.hediffSet.GetNotMissingParts()
                    .Where(p => p.def.defName.ToLower().Contains("hand") || p.def.defName.ToLower().Contains("foot"))
                    .ToList();
            }

            if (extremities.Any())
            {
                var targetPart = extremities.RandomElement();

                // Try frostbite hediff, fallback to damage
                var frostbiteHediff = DefDatabase<HediffDef>.GetNamedSilentFail("Frostbite");
                if (frostbiteHediff != null)
                {
                    var hediff = HediffMaker.MakeHediff(frostbiteHediff, pawn, targetPart);
                    hediff.Severity = Rand.Range(0.2f, 0.6f);
                    pawn.health.AddHediff(hediff);
                    Log.Message($"[Decompression] Applied frostbite to {targetPart.def.label}");
                }
                else
                {
                    // Fallback to cold damage
                    DamageInfo damageInfo = new DamageInfo(DamageDefOf.Blunt, Rand.Range(5f, 12f), 0f, -1f, null, targetPart);
                    pawn.TakeDamage(damageInfo);
                }
            }
        }

        private static void ApplyBodyPartDestruction(Pawn pawn)
        {
            // Target small parts that can be destroyed
            var destroyableParts = pawn.health.hediffSet.GetNotMissingParts()
                .Where(p => p.def.tags?.Any(tag =>
                    tag.defName.ToLower().Contains("digit") ||
                    tag.defName.ToLower().Contains("finger") ||
                    tag.defName.ToLower().Contains("toe")) == true)
                .ToList();

            if (destroyableParts.Any())
            {
                var targetPart = destroyableParts.RandomElement();

                // Apply enough damage to destroy the part
                float destructiveDamage = targetPart.def.GetMaxHealth(pawn) + 5f;
                DamageInfo damageInfo = new DamageInfo(DamageDefOf.Cut, destructiveDamage, 0f, -1f, null, targetPart);
                pawn.TakeDamage(damageInfo);
                Log.Message($"[Decompression] Destroyed {targetPart.def.label} due to critical decompression");
            }
        }
    }
}