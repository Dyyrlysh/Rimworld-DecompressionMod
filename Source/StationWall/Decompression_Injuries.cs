using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace StationWall
{
    internal class Decompression_Injuries
    {
        public static void ApplyBoneFractures(Pawn pawn, float severity)
        {
            Log.Message("[DecompressionDebug] Entered ApplyBoneFractures()");

            BodyPartRecord part = pawn.health.hediffSet.GetRandomNotMissingPart(DamageDefOf.Bullet, BodyPartHeight.Undefined, BodyPartDepth.Outside);
            if (part != null)
            {
                DamageInfo dinfo = new DamageInfo(DamageDefOf.Blunt, Mathf.Lerp(10f, 25f, severity), 0f, -1f, null, part);
                pawn.TakeDamage(dinfo);
            }
        }

        public static void ApplyOrganBruising(Pawn pawn, float severity)
        {
            Log.Message("[DecompressionDebug] Entered ApplyOrganBruising()");

            var organs = pawn.health.hediffSet.GetNotMissingParts()
                .Where(part => part.def.defName.Contains("Heart") ||
                     part.def.defName.Contains("Liver") ||
                     part.def.defName.Contains("Lung") ||
                     part.def.defName.Contains("Kidney"))
                .ToList();

            Log.Message("[DecompressionDebug] Targetable organs: " + string.Join(", ", organs.Select(o => o.def.defName)));

            if (organs.Any())
            {
                BodyPartRecord targetOrgan = organs.RandomElement();
                float damageAmount = Mathf.Lerp(5f, 12f, severity);

                DamageInfo dinfo = new DamageInfo(
                    DamageDefOf.Blunt,
                    damageAmount,
                    0f,
                    -1f,
                    null,
                    targetOrgan
                    );

                pawn.TakeDamage(dinfo);
            }
        }

        public static void ApplyInternalBleeding(Pawn pawn, float severity)
        {
            var organs = pawn.health.hediffSet.GetNotMissingParts()
                .Where(part => part.def.defName.Contains("Heart") ||
                     part.def.defName.Contains("Liver") ||
                     part.def.defName.Contains("Lung") ||
                     part.def.defName.Contains("Kidney"))
                .ToList();

            Log.Message("[DecompressionDebug] Targetable organs: " + string.Join(", ", organs.Select(o => o.def.defName)));

            if (organs.Any())
            {
                BodyPartRecord targetOrgan = organs.RandomElement();
                float damageAmount = Mathf.Lerp(0.1f, 0.5f, severity);

                DamageInfo dinfo = new DamageInfo(
                    DamageDefOf.Bullet,
                    damageAmount,
                    0f,
                    -1f,
                    null,
                    targetOrgan
                    );

                pawn.TakeDamage(dinfo);
            }
        }
    }
}
