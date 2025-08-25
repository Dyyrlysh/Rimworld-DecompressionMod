using RimWorld;
using UnityEngine;
using Verse;

public class Graphic_LinkedReinforcedWindow : Graphic_Single
{
    public override void Print(SectionLayer layer, Thing thing, float extraRotation)
    {
        // Determine which texture to use based on neighbors
        string texPath = GetTexturePath(thing);

        // Create material for this specific texture
        Material mat = MaterialPool.MatFrom(texPath, ShaderDatabase.Cutout, Color.white);

        // Print the plane
        Printer_Plane.PrintPlane(layer, thing.TrueCenter(), new Vector2(1f, 1f), mat, extraRotation);

        // Handle shadow if needed
        if (ShadowGraphic != null)
        {
            ShadowGraphic.Print(layer, thing, 0f);
        }
    }

    public override Material MatSingleFor(Thing thing)
    {
        string texPath = GetTexturePath(thing);
        return MaterialPool.MatFrom(texPath, ShaderDatabase.Cutout, Color.white);
    }

    private string GetTexturePath(Thing thing)
    {
        if (thing?.Map == null)
            return data.texPath; // fallback to base texture

        IntVec3 pos = thing.Position;
        Map map = thing.Map;

        bool n = IsWindowOrWall(pos + IntVec3.North, map);
        bool e = IsWindowOrWall(pos + IntVec3.East, map);
        bool s = IsWindowOrWall(pos + IntVec3.South, map);
        bool w = IsWindowOrWall(pos + IntVec3.West, map);

        string suffix;
        if (n && s && !e && !w)
            suffix = "_NS";
        else if (e && w && !n && !s)
            suffix = "_EW";
        else if (n && !s)
            suffix = "_End_S";
        else if (s && !n)
            suffix = "_End_N";
        else if (e && !w)
            suffix = "_End_W";
        else if (w && !e)
            suffix = "_End_E";
        else
            suffix = "_NS"; // fallback

        return data.texPath + suffix;
    }

    private bool IsWindowOrWall(IntVec3 c, Map map)
    {
        if (!c.InBounds(map)) return false;
        var edifice = c.GetEdifice(map);
        if (edifice == null) return false;
        return edifice.def.defName == "ReinforcedWindow" ||
               edifice.def.defName == "GravshipHull" ||
               edifice.def.defName == "StationWall";
    }
}