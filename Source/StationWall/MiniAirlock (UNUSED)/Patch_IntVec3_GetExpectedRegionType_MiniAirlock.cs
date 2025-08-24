using Verse;

namespace DecompressionMod
{
    [StaticConstructorOnStartup]
    internal static class AirlockLoadNudge
    {
        static AirlockLoadNudge()
        {
            // Touch the type so it JITs early; no patching, no side effects.
            _ = typeof(Building_MiniAirlock);
        }
    }
}