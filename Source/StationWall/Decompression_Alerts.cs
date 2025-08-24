using RimWorld;
using Verse;

namespace DecompressionMod
{
    public static class Decompression_Alerts
    {
        public static void SendDecompressionAlert(DecompressionEvent decompressionEvent, Map map)
        {
            switch (decompressionEvent.severity)
            {
                case DecompressionSeverity.Major:
                    Find.LetterStack.ReceiveLetter(
                        "Decompression",
                        "A pressurized environment has been exposed to vacuum, causing an intense decompression!\n\nAny colonists inside the environment will be affected by the breach, depending on their vacuum resistance. Make sure your orbital structures have adequate protection from the harshness of space, such as airlocks and vac barriers.",
                        LetterDefOf.ThreatSmall,
                        new TargetInfo(decompressionEvent.breachPoint, map)
                    );
                    Find.TickManager.Pause();
                    break;

                case DecompressionSeverity.Minor:
                    Messages.Message(
                        "A minor decompression has occurred!",
                        new TargetInfo(decompressionEvent.breachPoint, map),
                        MessageTypeDefOf.CautionInput,
                        false
                    );
                    break;
            }

            Log.Message($"[Decompression] Sent {decompressionEvent.severity} alert");
        }
    }
}