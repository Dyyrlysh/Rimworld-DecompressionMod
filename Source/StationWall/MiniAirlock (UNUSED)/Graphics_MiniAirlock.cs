using UnityEngine;
using Verse;

namespace DecompressionMod
{
    public class Graphic_MiniAirlock : Graphic
    {
        // Base airlock graphics (2 textures: NS and EW orientations)
        private Material baseMat_NS; // North-South orientation
        private Material baseMat_EW; // East-West orientation

        // Door graphics (1 texture, positioned dynamically)
        private Material doorMat;

        // Indicator graphics (2 textures: red and green)
        private Material indicatorMat_Red;
        private Material indicatorMat_Green;

        // Layer offsets for proper rendering order
        private const float DoorLayerOffset = 0.02f;
        private const float IndicatorLayerOffset = 0.04f;

        public override void Init(GraphicRequest req)
        {
            data = req.graphicData;
            path = req.path;
            color = req.color;
            colorTwo = req.colorTwo;
            drawSize = req.drawSize;

            try
            {
                // Load base airlock textures
                baseMat_NS = MaterialPool.MatFrom($"{path}_Base_NS", req.shader, color);
                baseMat_EW = MaterialPool.MatFrom($"{path}_Base_EW", req.shader, color);

                // Load door texture
                doorMat = MaterialPool.MatFrom($"{path}_Door", req.shader, color);

                // Load indicator textures - try with fallback
                try
                {
                    indicatorMat_Red = MaterialPool.MatFrom($"{path}_Indicator_Red", req.shader);
                }
                catch
                {
                    indicatorMat_Red = MaterialPool.MatFrom("UI/Icons/ThingCategories/Weapons", req.shader); // Red fallback
                }

                try
                {
                    indicatorMat_Green = MaterialPool.MatFrom($"{path}_Indicator_Green", req.shader);
                }
                catch
                {
                    indicatorMat_Green = MaterialPool.MatFrom("UI/Icons/ThingCategories/PlantFoodRaw", req.shader); // Green fallback
                }

                Log.Message($"[MiniAirlock] Successfully initialized graphics for {path}");
            }
            catch (System.Exception ex)
            {
                Log.Error($"[MiniAirlock] Error initializing graphics: {ex}");
            }
        }

        public override Material MatAt(Rot4 rot, Thing thing = null)
        {
            // Return base material based on orientation
            if (rot == Rot4.North || rot == Rot4.South)
                return baseMat_NS;
            else
                return baseMat_EW;
        }

        public override void DrawWorker(Vector3 loc, Rot4 rot, ThingDef thingDef, Thing thing, float extraRotation)
        {
            Building_MiniAirlock airlock = thing as Building_MiniAirlock;
            if (airlock == null)
            {
                // Fallback for blueprints/ghosts
                base.DrawWorker(loc, rot, thingDef, thing, extraRotation);
                return;
            }

            Mesh mesh = MeshAt(rot);
            Quaternion quat = QuatFromRot(rot);
            if (extraRotation != 0f)
            {
                quat *= Quaternion.Euler(Vector3.up * extraRotation);
            }
            loc += DrawOffset(rot);

            // 1. Draw base airlock
            Material baseMat = MatAt(rot, thing);
            Graphics.DrawMesh(mesh, loc, quat, baseMat, 0);

            // 2. Draw doors if open
            DrawDoors(airlock, loc, rot, quat, mesh);

            // 3. Draw indicators
            DrawIndicators(airlock, loc, rot, quat, mesh);

            // 4. Draw shadow
            if (ShadowGraphic != null)
            {
                ShadowGraphic.DrawWorker(loc, rot, thingDef, thing, extraRotation);
            }
        }

        private void DrawDoors(Building_MiniAirlock airlock, Vector3 loc, Rot4 rot, Quaternion quat, Mesh mesh)
        {
            if (!airlock.InnerDoorOpen && !airlock.OuterDoorOpen) return; // No doors to draw when closed
            if (doorMat == null) return; // Skip if door material failed to load

            Vector3 doorLoc = loc;
            doorLoc.y += DoorLayerOffset;

            // Use smaller mesh for doors
            Mesh doorMesh = MeshPool.GridPlane(new Vector2(0.4f, 0.4f));

            // Calculate door positions based on rotation
            Vector3 sideADoorOffset, sideBDoorOffset;
            GetDoorOffsets(rot, out sideADoorOffset, out sideBDoorOffset);

            // Draw inner door if open (for now, map inner to side A)
            if (airlock.InnerDoorOpen)
            {
                Vector3 innerDoorPos = doorLoc + sideADoorOffset;
                Graphics.DrawMesh(doorMesh, innerDoorPos, quat, doorMat, 0);
            }

            // Draw outer door if open (for now, map outer to side B)
            if (airlock.OuterDoorOpen)
            {
                Vector3 outerDoorPos = doorLoc + sideBDoorOffset;
                Graphics.DrawMesh(doorMesh, outerDoorPos, quat, doorMat, 0);
            }
        }

        private void DrawIndicators(Building_MiniAirlock airlock, Vector3 loc, Rot4 rot, Quaternion quat, Mesh mesh)
        {
            // Skip indicators if materials failed to load
            if (indicatorMat_Red == null || indicatorMat_Green == null) return;

            Vector3 indicatorLoc = loc;
            indicatorLoc.y += IndicatorLayerOffset;

            // Use smaller mesh for indicators
            Mesh indicatorMesh = MeshPool.GridPlane(new Vector2(0.2f, 0.2f));

            // Calculate indicator positions based on rotation
            Vector3 sideAIndicatorOffset, sideBIndicatorOffset;
            GetIndicatorOffsets(rot, out sideAIndicatorOffset, out sideBIndicatorOffset);

            // Get the actual cells each indicator is monitoring
            IntVec3 sideACell = GetSideACell(airlock.Position, rot);
            IntVec3 sideBCell = GetSideBCell(airlock.Position, rot);

            // Check vacuum levels for each side
            float sideAVacuum = 1f; // Default to vacuum if out of bounds
            float sideBVacuum = 1f;

            if (sideACell.InBounds(airlock.Map))
            {
                sideAVacuum = sideACell.GetVacuum(airlock.Map);
            }

            if (sideBCell.InBounds(airlock.Map))
            {
                sideBVacuum = sideBCell.GetVacuum(airlock.Map);
            }

            // Determine indicator colors (red if vacuum > 50%, green if <= 50%)
            bool sideAIsVacuum = sideAVacuum > 0.5f;
            bool sideBIsVacuum = sideBVacuum > 0.5f;

            // Debug logging (only log occasionally to avoid spam)
            if (Find.TickManager.TicksGame % 60 == 0) // Once per second
            {
                Log.Message($"[MiniAirlock] Side A cell {sideACell} vacuum: {sideAVacuum:F2} -> {(sideAIsVacuum ? "RED" : "GREEN")}");
                Log.Message($"[MiniAirlock] Side B cell {sideBCell} vacuum: {sideBVacuum:F2} -> {(sideBIsVacuum ? "RED" : "GREEN")}");
            }

            // Draw side A indicator
            Vector3 sideAIndicatorPos = indicatorLoc + sideAIndicatorOffset;
            Material sideAIndicatorMat = sideAIsVacuum ? indicatorMat_Red : indicatorMat_Green;
            Graphics.DrawMesh(indicatorMesh, sideAIndicatorPos, quat, sideAIndicatorMat, 0);

            // Draw side B indicator  
            Vector3 sideBIndicatorPos = indicatorLoc + sideBIndicatorOffset;
            Material sideBIndicatorMat = sideBIsVacuum ? indicatorMat_Red : indicatorMat_Green;
            Graphics.DrawMesh(indicatorMesh, sideBIndicatorPos, quat, sideBIndicatorMat, 0);
        }

        private void GetDoorOffsets(Rot4 rot, out Vector3 sideADoorOffset, out Vector3 sideBDoorOffset)
        {
            // Doors slide perpendicular to airlock facing
            // Side A door slides toward side A, Side B door slides toward side B

            switch (rot.AsInt)
            {
                case 0: // North - doors slide east/west
                    sideADoorOffset = new Vector3(-0.3f, 0f, 0f); // Side A door slides west
                    sideBDoorOffset = new Vector3(0.3f, 0f, 0f);  // Side B door slides east
                    break;
                case 1: // East - doors slide north/south  
                    sideADoorOffset = new Vector3(0f, 0f, 0.3f);  // Side A door slides north
                    sideBDoorOffset = new Vector3(0f, 0f, -0.3f); // Side B door slides south
                    break;
                case 2: // South - doors slide east/west
                    sideADoorOffset = new Vector3(0.3f, 0f, 0f);  // Side A door slides east
                    sideBDoorOffset = new Vector3(-0.3f, 0f, 0f); // Side B door slides west
                    break;
                case 3: // West - doors slide north/south
                    sideADoorOffset = new Vector3(0f, 0f, -0.3f); // Side A door slides south
                    sideBDoorOffset = new Vector3(0f, 0f, 0.3f);  // Side B door slides north
                    break;
                default:
                    sideADoorOffset = Vector3.zero;
                    sideBDoorOffset = Vector3.zero;
                    break;
            }
        }

        private void GetIndicatorOffsets(Rot4 rot, out Vector3 sideAOffset, out Vector3 sideBOffset)
        {
            // Position indicators facing the two walkable passage directions
            // Side A and Side B are just the two opposite sides the airlock connects

            switch (rot.AsInt)
            {
                case 0: // North - indicators face north/south (along the passage)
                    sideAOffset = new Vector3(0f, 0f, -0.4f); // South side
                    sideBOffset = new Vector3(0f, 0f, 0.4f);  // North side
                    break;
                case 1: // East - indicators face east/west (along the passage)
                    sideAOffset = new Vector3(-0.4f, 0f, 0f); // West side
                    sideBOffset = new Vector3(0.4f, 0f, 0f);  // East side
                    break;
                case 2: // South - indicators face north/south (along the passage)
                    sideAOffset = new Vector3(0f, 0f, 0.4f);  // North side
                    sideBOffset = new Vector3(0f, 0f, -0.4f); // South side
                    break;
                case 3: // West - indicators face east/west (along the passage)
                    sideAOffset = new Vector3(0.4f, 0f, 0f);  // East side
                    sideBOffset = new Vector3(-0.4f, 0f, 0f); // West side
                    break;
                default:
                    sideAOffset = Vector3.zero;
                    sideBOffset = Vector3.zero;
                    break;
            }
        }

        private IntVec3 GetSideACell(IntVec3 airlockPos, Rot4 rot)
        {
            // Return cell on side A
            switch (rot.AsInt)
            {
                case 0: // North - side A is south
                    return airlockPos + IntVec3.South;
                case 1: // East - side A is west  
                    return airlockPos + IntVec3.West;
                case 2: // South - side A is north
                    return airlockPos + IntVec3.North;
                case 3: // West - side A is east
                    return airlockPos + IntVec3.East;
                default:
                    return airlockPos;
            }
        }

        private IntVec3 GetSideBCell(IntVec3 airlockPos, Rot4 rot)
        {
            // Return cell on side B
            switch (rot.AsInt)
            {
                case 0: // North - side B is north
                    return airlockPos + IntVec3.North;
                case 1: // East - side B is east
                    return airlockPos + IntVec3.East;
                case 2: // South - side B is south 
                    return airlockPos + IntVec3.South;
                case 3: // West - side B is west
                    return airlockPos + IntVec3.West;
                default:
                    return airlockPos;
            }
        }

        public override Graphic GetColoredVersion(Shader newShader, Color newColor, Color newColorTwo)
        {
            // Create new colored version
            return GraphicDatabase.Get<Graphic_MiniAirlock>(path, newShader, drawSize, newColor, newColorTwo, data);
        }

        public override string ToString()
        {
            return $"MiniAirlock(path={path}, color={color})";
        }
    }
}