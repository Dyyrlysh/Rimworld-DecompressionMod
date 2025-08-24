using Verse;
using RimWorld;
using UnityEngine;

namespace StationMod
{
    public class CompOxygenReactiveLight : ThingComp
    {
        private CompGlower glower;
        private CompPowerTrader powerComp;
        private bool isDepressurized;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            powerComp = parent.GetComp<CompPowerTrader>();
            glower = parent.GetComp<CompGlower>();
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            CheckAtmosphereStatus();
        }

        private void CheckAtmosphereStatus()
        {
            Room room = parent.GetRoom(); // Re-fetch each tick for accuracy
            if (room == null || powerComp == null || !powerComp.PowerOn || glower == null) return;

            var oxygenComp = room.GetRoomComponent<CompOxygenRoom>();
            if (oxygenComp != null)
            {
                bool noAtmosphere = oxygenComp.OxygenLevel < 0.2f;

                if (noAtmosphere != isDepressurized)
                {
                    isDepressurized = noAtmosphere;
                    UpdateVisuals();
                }
            }
        }

        private void UpdateVisuals()
        {
            if (isDepressurized)
            {
                glower.Props.glowColor = new ColorInt(255, 0, 0, 255); // Red glow
                parent.Graphic = GraphicDatabase.Get<Graphic_Multi>(
                    "Things/Building/O2Lamp_Red", ShaderDatabase.Cutout, new Vector2(1f, 1f), Color.white);
            }
            else
            {
                glower.Props.glowColor = new ColorInt(255, 255, 255, 255); // White glow
                parent.Graphic = GraphicDatabase.Get<Graphic_Multi>(
                    "Things/Building/O2Lamp_White", ShaderDatabase.Cutout, new Vector2(1f, 1f), Color.white);
            }

            parent.BroadcastCompPropertiesChanged(); // Refresh glower component
            if (parent.Map != null)
                parent.Map.mapDrawer.MapMeshDirty(parent.Position, MapMeshFlag.Things | MapMeshFlag.Buildings);
        }
    }
}