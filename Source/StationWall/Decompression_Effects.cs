using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace DecompressionMod
{
    public static class Decompression_Effects
    {
        public static void ApplyDecompressionEffects(DecompressionEvent decompressionEvent, Map map)
        {
            var allPawns = map.mapPawns.AllPawnsSpawned.ToList();
            var pawnsInRoom = allPawns.Where(p => decompressionEvent.sourceRoom.Cells.Contains(p.Position)).ToList();

            // Check doorways too
            foreach (var room in map.regionGrid.AllRooms)
            {
                if (room.IsDoorway)
                {
                    var doorCell = room.Cells.FirstOrDefault();
                    if (doorCell != IntVec3.Invalid)
                    {
                        var door = doorCell.GetDoor(map);
                        if (door != null && door.Open)
                        {
                            bool connectsToSourceRoom = false;
                            foreach (var adjCell in GenAdj.CardinalDirections.Select(dir => door.Position + dir))
                            {
                                if (adjCell.InBounds(map) && adjCell.GetRoom(map) == decompressionEvent.sourceRoom)
                                {
                                    connectsToSourceRoom = true;
                                    break;
                                }
                            }

                            if (connectsToSourceRoom)
                            {
                                var doorwayPawns = allPawns.Where(p => room.Cells.Contains(p.Position)).ToList();
                                pawnsInRoom.AddRange(doorwayPawns);
                            }
                        }
                    }
                }
            }

            Log.Message($"[Decompression] Applying effects to {pawnsInRoom.Distinct().Count()} pawns");

            // Apply effects to pawns
            foreach (var pawn in pawnsInRoom.Distinct())
            {
                ApplyDecompressionToPawn(pawn, decompressionEvent, map);
            }

            // Apply effects to furniture
            ApplyDecompressionToFurniture(decompressionEvent, map);
        }

        private static void ApplyDecompressionToFurniture(DecompressionEvent decompressionEvent, Map map)
        {
            var affectedFurniture = new List<Thing>();
            var blacklistedItems = new List<Thing>(); // Items that can't move but should take damage

            Log.Message($"[Decompression] Scanning {decompressionEvent.sourceRoom.Cells.Count()} cells for furniture");

            // Find all reinstallable furniture in the affected room
            foreach (var cell in decompressionEvent.sourceRoom.Cells)
            {
                var things = cell.GetThingList(map);
                Log.Message($"[Decompression] Cell {cell} has {things.Count} things");

                foreach (var thing in things)
                {
                    Log.Message($"[Decompression] Found thing: {thing.def.defName}, type: {thing.GetType().Name}");

                    if (IsReinstallableFurniture(thing))
                    {
                        affectedFurniture.Add(thing);
                        Log.Message($"[Decompression] Added {thing.def.defName} to affected list");
                    }
                    else if (thing is Building && IsBlacklistedFurniture(thing))
                    {
                        blacklistedItems.Add(thing);
                        Log.Message($"[Decompression] Added {thing.def.defName} to blacklisted damage list");
                    }
                    // ADD THIS NEW SECTION:
                    else if (IsDisplaceableItem(thing))
                    {
                        affectedFurniture.Add(thing); // Reuse the same list
                        Log.Message($"[Decompression] Added item {thing.def.defName} to displacement list");
                    }
                }
            }

            // Also check doorways connected to the room
            foreach (var room in map.regionGrid.AllRooms)
            {
                if (room.IsDoorway)
                {
                    var doorCell = room.Cells.FirstOrDefault();
                    if (doorCell != IntVec3.Invalid)
                    {
                        var door = doorCell.GetDoor(map);
                        if (door != null && door.Open)
                        {
                            bool connectsToSourceRoom = false;
                            foreach (var adjCell in GenAdj.CardinalDirections.Select(dir => door.Position + dir))
                            {
                                if (adjCell.InBounds(map) && adjCell.GetRoom(map) == decompressionEvent.sourceRoom)
                                {
                                    connectsToSourceRoom = true;
                                    break;
                                }
                            }

                            if (connectsToSourceRoom)
                            {
                                var doorwayThings = room.Cells.SelectMany(c => c.GetThingList(map));
                                foreach (var thing in doorwayThings)
                                {
                                    if (IsReinstallableFurniture(thing))
                                    {
                                        affectedFurniture.Add(thing);
                                    }
                                    else if (thing is Building && IsBlacklistedFurniture(thing))
                                    {
                                        blacklistedItems.Add(thing);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            Log.Message($"[Decompression] Found {affectedFurniture.Count} pieces of reinstallable furniture and {blacklistedItems.Count} blacklisted items");

            // Simple ripple effect: sort by distance to breach, process in small groups
            var sortedFurniture = affectedFurniture.Distinct()
                .OrderBy(f => f.Position.DistanceTo(decompressionEvent.breachPoint))
                .ToList();

            Log.Message($"[Decompression] Processing furniture in ripple order (closest first)");

            // Process in small groups to create subtle ripple effect
            for (int i = 0; i < sortedFurniture.Count; i += 2) // Groups of 2
            {
                var group = sortedFurniture.Skip(i).Take(2);
                foreach (var furniture in group)
                {
                    if (furniture?.Spawned == true)
                    {
                        ApplyDecompressionToFurniturePiece(furniture, decompressionEvent, map);
                    }
                }
            }

            // Apply minor damage to blacklisted items (shelves, wall-mounted, etc.)
            foreach (var item in blacklistedItems.Distinct())
            {
                if (item?.Spawned == true)
                {
                    ApplyMinorDamageToBlacklistedItem(item, decompressionEvent);
                }
            }

            // Apply damage to interior walls
            ApplyWallDamage(decompressionEvent, map);

            // Disable electronics temporarily
            ApplyElectronicsDisruption(decompressionEvent, map);
        }

        private static bool IsBlacklistedFurniture(Thing thing)
        {
            if (!(thing is Building building)) return false;

            string defName = building.def.defName.ToLower();

            // Wall-mounted things
            if (building.def.building?.canPlaceOverWall == true ||
                defName.Contains("pump") ||
                defName.Contains("vent") ||
                defName.Contains("cooler") ||
                defName.Contains("heater") ||
                defName.Contains("conduit") ||
                defName.Contains("wallmount"))
            {
                return true;
            }

            // Storage buildings
            if (building.def.building?.fixedStorageSettings != null ||
                defName.Contains("shelf") ||
                defName.Contains("rack") ||
                defName.Contains("storage") ||
                defName.Contains("stockpile"))
            {
                return true;
            }

            // Temperature control structures (vents, heaters, coolers, etc.)
            if (defName.Contains("temperature") ||
                defName.Contains("aircon") ||
                defName.Contains("hvac") ||
                defName.Contains("climate") ||
                building.def.building?.ai_chillDestination == true)
            {
                return true;
            }

            return false;
        }

        private static void ApplyElectronicsDisruption(DecompressionEvent decompressionEvent, Map map)
        {
            var electronicsToDisrupt = new List<Thing>();

            Log.Message($"[Decompression] Scanning {decompressionEvent.sourceRoom.Cells.Count()} cells for electronics");

            // Find all electronics in the affected area
            foreach (var cell in decompressionEvent.sourceRoom.Cells)
            {
                var things = cell.GetThingList(map);
                foreach (var thing in things)
                {
                    Log.Message($"[Decompression] Checking thing: {thing.def.defName} (type: {thing.GetType().Name})");

                    if (thing is Building building && IsElectronicDevice(building))
                    {
                        electronicsToDisrupt.Add(thing);
                        Log.Message($"[Decompression] Added electronic device: {building.def.defName}");
                    }
                }
            }

            Log.Message($"[Decompression] Found {electronicsToDisrupt.Count} electronic devices to disrupt");

            foreach (var device in electronicsToDisrupt)
            {
                if (device?.Spawned == true)
                {
                    DisruptElectronicDevice(device, decompressionEvent);
                }
            }

            // Add some extra visual effects even if no electronics found
            if (electronicsToDisrupt.Count == 0)
            {
                // Create some random lightning effects in the room for atmosphere
                var randomCells = decompressionEvent.sourceRoom.Cells.InRandomOrder().Take(3);
                foreach (var cell in randomCells)
                {
                    if (Rand.Chance(0.3f))
                    {
                        FleckMaker.ThrowLightningGlow(cell.ToVector3(), map, 1.0f);
                        Log.Message($"[Decompression] Added atmospheric lightning effect at {cell}");
                    }
                }
            }
        }

        private static bool IsElectronicDevice(Building building)
        {
            Log.Message($"[Decompression] Testing {building.def.defName} for electronics...");

            string defName = building.def.defName.ToLower();

            // EXCLUDE temperature control from electronics (they should only take minor damage)
            if (defName.Contains("vent") ||
                defName.Contains("cooler") ||
                defName.Contains("heater") ||
                defName.Contains("pump") ||
                defName.Contains("temperature") ||
                defName.Contains("aircon") ||
                defName.Contains("hvac") ||
                defName.Contains("climate"))
            {
                Log.Message($"[Decompression] {building.def.defName} is temperature control - NOT ELECTRONIC for disruption");
                return false;
            }

            // Check if it has power components (proper way to detect powered devices)
            var powerComp = building.TryGetComp<CompPowerTrader>();
            if (powerComp != null)
            {
                Log.Message($"[Decompression] {building.def.defName} has CompPowerTrader - IS ELECTRONIC");
                return true;
            }

            // Check for power properties in the def
            var powerProps = building.def.GetCompProperties<CompProperties_Power>();
            if (powerProps != null)
            {
                Log.Message($"[Decompression] {building.def.defName} has CompProperties_Power - IS ELECTRONIC");
                return true;
            }

            // Check if it has any comp that suggests it's powered
            var allComps = building.AllComps;
            foreach (var comp in allComps)
            {
                if (comp.GetType().Name.Contains("Power"))
                {
                    Log.Message($"[Decompression] {building.def.defName} has power-related comp: {comp.GetType().Name} - IS ELECTRONIC");
                    return true;
                }
            }

            // Expanded electronic device name detection (excluding temp control)
            if (defName.Contains("console") ||
                defName.Contains("terminal") ||
                defName.Contains("computer") ||
                defName.Contains("screen") ||
                defName.Contains("monitor") ||
                defName.Contains("research") ||
                defName.Contains("comm") ||
                defName.Contains("radio") ||
                defName.Contains("scanner") ||
                defName.Contains("fabricator") ||
                defName.Contains("workstation") ||
                defName.Contains("bench") ||
                defName.Contains("station") ||
                defName.Contains("printer") ||
                defName.Contains("analyser") ||
                defName.Contains("analyzer"))
            {
                Log.Message($"[Decompression] {building.def.defName} matches electronic name pattern - IS ELECTRONIC");
                return true;
            }

            Log.Message($"[Decompression] {building.def.defName} is NOT electronic");
            return false;
        }

        private static void DisruptElectronicDevice(Thing device, DecompressionEvent decompressionEvent)
        {
            var building = device as Building;
            if (building == null) return;

            bool wasDisrupted = false;

            // Method 1: Temporarily turn off powered devices
            var powerTrader = building.TryGetComp<CompPowerTrader>();
            if (powerTrader != null && powerTrader.PowerOn)
            {
                powerTrader.PowerOn = false;
                wasDisrupted = true;
                Log.Message($"[Decompression] Temporarily powered off: {building.def.defName}");

                // Note: In a full implementation, you'd want to create a component to track restoration
                // For now, devices will stay off until manually turned back on or repaired
            }

            // Method 2: Cause temporary breakdown for breakdownable items
            var breakdown = building.TryGetComp<CompBreakdownable>();
            if (breakdown != null && !breakdown.BrokenDown)
            {
                breakdown.DoBreakdown();
                wasDisrupted = true;
                Log.Message($"[Decompression] Caused temporary breakdown: {building.def.defName}");
            }

            // Method 3: If no power components, apply minor EMP damage
            if (!wasDisrupted)
            {
                float empDamage = decompressionEvent.severity == DecompressionSeverity.Major ?
                    Rand.Range(5f, 12f) : Rand.Range(3f, 8f);

                DamageInfo damageInfo = new DamageInfo(
                    DamageDefOf.EMP,
                    empDamage,
                    0f, -1f, null, null, null,
                    DamageInfo.SourceCategory.ThingOrUnknown
                );

                device.TakeDamage(damageInfo);
                Log.Message($"[Decompression] Applied EMP damage to: {building.def.defName} ({empDamage:F1} damage)");
            }

            // Always add visual effect
            FleckMaker.ThrowLightningGlow(device.DrawPos, device.Map, 1.5f);
        }

        private static void ApplyMinorDamageToBlacklistedItem(Thing item, DecompressionEvent decompressionEvent)
        {
            float minorDamage = decompressionEvent.severity == DecompressionSeverity.Major ?
                Rand.Range(5f, 12f) : Rand.Range(2f, 8f);

            DamageInfo damageInfo = new DamageInfo(
                DamageDefOf.Blunt,
                minorDamage,
                0f, -1f, null, null, null,
                DamageInfo.SourceCategory.ThingOrUnknown
            );

            item.TakeDamage(damageInfo);
            Log.Message($"[Decompression] Applied {minorDamage:F1} minor damage to blacklisted {item.def.defName}");
        }

        private static void ApplyWallDamage(DecompressionEvent decompressionEvent, Map map)
        {
            var roomCells = decompressionEvent.sourceRoom.Cells.ToList();
            var wallCells = new List<IntVec3>();

            // Find interior walls (walls that are adjacent to room cells)
            foreach (var cell in roomCells)
            {
                foreach (var adjCell in GenAdj.CardinalDirections.Select(dir => cell + dir))
                {
                    if (adjCell.InBounds(map) && !roomCells.Contains(adjCell))
                    {
                        var edifice = adjCell.GetEdifice(map);
                        if (edifice?.def?.building?.isEdifice == true &&
                            (edifice.def.defName.ToLower().Contains("wall") || edifice.def.building.isWall))
                        {
                            wallCells.Add(adjCell);
                        }
                    }
                }
            }

            if (!wallCells.Any()) return;

            // Scale damage probability based on room size - INCREASED percentages
            int roomSize = roomCells.Count;
            float wallDamageChance = roomSize <= 20 ? 1.0f : // Small rooms: 100% of walls
                                   roomSize <= 50 ? 0.85f : // Medium rooms: 85% of walls
                                   roomSize <= 100 ? 0.7f : // Large rooms: 70% of walls
                                   0.5f; // Very large rooms: 50% of walls

            Log.Message($"[Decompression] Found {wallCells.Count} wall cells, damage chance: {wallDamageChance:F1} (room size: {roomSize})");

            foreach (var wallCell in wallCells.Distinct())
            {
                if (Rand.Chance(wallDamageChance))
                {
                    var wall = wallCell.GetEdifice(map);
                    if (wall?.Spawned == true)
                    {
                        float wallDamage = decompressionEvent.severity == DecompressionSeverity.Major ?
                            Rand.Range(8f, 20f) : Rand.Range(3f, 12f);

                        DamageInfo damageInfo = new DamageInfo(
                            DamageDefOf.Blunt,
                            wallDamage,
                            0f, -1f, null, null, null,
                            DamageInfo.SourceCategory.ThingOrUnknown
                        );

                        wall.TakeDamage(damageInfo);
                        Log.Message($"[Decompression] Applied {wallDamage:F1} damage to wall {wall.def.defName} at {wallCell}");
                    }
                }
            }
        }

        private static void ApplyDecompressionToFurniturePiece(Thing furniture, DecompressionEvent decompressionEvent, Map map)
        {
            if (furniture?.Spawned != true) return;

            float severityMultiplier = decompressionEvent.severity == DecompressionSeverity.Major ? 1.0f : 0.6f;

            // Try to displace the furniture
            bool displaced = TryDisplaceFurniture(furniture, decompressionEvent, map, severityMultiplier);

            // Apply damage
            ApplyFurnitureDamage(furniture, decompressionEvent, severityMultiplier, displaced);

            Log.Message($"[Decompression] Affected furniture: {furniture.def.defName} (displaced: {displaced})");
        }

        private static void ProcessFurnitureInWaves(List<Thing> sortedFurniture, DecompressionEvent decompressionEvent, Map map)
        {
            float severityMultiplier = decompressionEvent.severity == DecompressionSeverity.Major ? 1.0f : 0.6f;

            // Process furniture in small batches to create ripple effect
            const int batchSize = 3; // Process a few pieces at a time

            for (int i = 0; i < sortedFurniture.Count; i += batchSize)
            {
                var batch = sortedFurniture.Skip(i).Take(batchSize).ToList();

                Log.Message($"[Decompression] Processing wave {(i / batchSize) + 1}: {batch.Count} items");

                foreach (var furniture in batch)
                {
                    // Double-check furniture still exists and is spawned
                    if (furniture?.Spawned == true && furniture.Map == map)
                    {
                        ApplyDecompressionToSingleFurniturePiece(furniture, decompressionEvent, map, severityMultiplier);
                    }
                    else
                    {
                        Log.Message($"[Decompression] Skipping {furniture?.def?.defName} - no longer spawned");
                    }
                }

                // Small delay between waves to allow positions to clear
                // In a real implementation, you might want to use coroutines or a tick-based system
                // For now, we'll process all at once but in the correct order
            }
        }

        private static void ApplyDecompressionToSingleFurniturePiece(Thing furniture, DecompressionEvent decompressionEvent, Map map, float severityMultiplier)
        {
            if (furniture?.Spawned != true) return;

            // Try to displace the furniture (now processed in correct order)
            bool displaced = TryDisplaceFurniture(furniture, decompressionEvent, map, severityMultiplier);

            // Apply damage (only if furniture still exists after displacement attempt)
            if (furniture?.Spawned == true)
            {
                ApplyFurnitureDamage(furniture, decompressionEvent, severityMultiplier, displaced);
            }

            Log.Message($"[Decompression] Affected furniture: {furniture?.def?.defName} (displaced: {displaced})");
        }

        private static bool IsReinstallableFurniture(Thing thing)
        {
            // Must be a building
            if (!(thing is Building building)) return false;

            // Must be minifiable (can be uninstalled/reinstalled)
            if (!building.def.Minifiable) return false;

            // Debug logging to see what we're checking
            Log.Message($"[Decompression] Checking furniture: {building.def.defName}, minifiable: {building.def.Minifiable}, isEdifice: {building.def.building?.isEdifice}");

            // Skip wall-mounted things FIRST (before edifice check)
            if (building.def.building?.canPlaceOverWall == true ||
                building.def.defName.ToLower().Contains("pump") ||
                building.def.defName.ToLower().Contains("vent") ||
                building.def.defName.ToLower().Contains("cooler") ||
                building.def.defName.ToLower().Contains("heater") ||
                building.def.defName.ToLower().Contains("conduit") ||
                building.def.defName.ToLower().Contains("wallmount"))
            {
                Log.Message($"[Decompression] Skipping {building.def.defName} - wall mounted/HVAC");
                return false;
            }

            // Skip storage buildings (shelves, etc.)
            if (building.def.building?.fixedStorageSettings != null ||
                building.def.defName.ToLower().Contains("shelf") ||
                building.def.defName.ToLower().Contains("rack") ||
                building.def.defName.ToLower().Contains("storage") ||
                building.def.defName.ToLower().Contains("stockpile"))
            {
                Log.Message($"[Decompression] Skipping {building.def.defName} - storage building");
                return false;
            }

            // For edifices, we need to be more selective - allow furniture edifices but not structural ones
            if (building.def.building?.isEdifice == true)
            {
                // Allow common furniture types even if they're edifices
                string defName = building.def.defName.ToLower();
                bool isFurnitureEdifice = defName.Contains("bed") ||
                                        defName.Contains("chair") ||
                                        defName.Contains("table") ||
                                        defName.Contains("dresser") ||
                                        defName.Contains("lamp") ||
                                        defName.Contains("torch") ||
                                        defName.Contains("sculpture") ||
                                        defName.Contains("art") ||
                                        defName.Contains("bench") ||
                                        defName.Contains("workbench") ||
                                        defName.Contains("stool") ||
                                        defName.Contains("armchair") ||
                                        defName.Contains("dining") ||
                                        defName.Contains("research") ||
                                        defName.Contains("crafting");

                if (!isFurnitureEdifice)
                {
                    // Skip structural edifices like walls, doors, etc.
                    if (defName.Contains("wall") ||
                        defName.Contains("door") ||
                        defName.Contains("gate") ||
                        defName.Contains("barrier"))
                    {
                        Log.Message($"[Decompression] Skipping {building.def.defName} - structural edifice");
                        return false;
                    }
                }
            }

            // Skip things that are too heavy or structural (affects roof collapse significantly)
            if (building.def.building?.roofCollapseDamageMultiplier > 1.5f)
            {
                Log.Message($"[Decompression] Skipping {building.def.defName} - affects roof collapse significantly");
                return false;
            }

            // Additional safety checks for clearly structural elements
            if (building.def.defName.ToLower().Contains("wall") ||
                building.def.defName.ToLower().Contains("door"))
            {
                Log.Message($"[Decompression] Skipping {building.def.defName} - structural element");
                return false;
            }

            Log.Message($"[Decompression] {building.def.defName} PASSED furniture check!");
            return true;
        }

        private static void ApplyFurnitureDamage(Thing furniture, DecompressionEvent decompressionEvent, float severityMultiplier, bool wasDisplaced)
        {
            if (furniture?.Spawned != true) return;

            float baseDamage = 0f;
            switch (decompressionEvent.severity)
            {
                case DecompressionSeverity.Major:
                    baseDamage = Rand.Range(15f, 35f);
                    break;
                case DecompressionSeverity.Minor:
                    baseDamage = Rand.Range(5f, 20f);
                    break;
            }

            // Reduce damage for lighter furniture types
            string defName = furniture.def.defName.ToLower();
            float damageReduction = 1.0f;

            if (defName.Contains("bed") || defName.Contains("sleeping"))
            {
                damageReduction = 0.9f; // 10% less damage (beds are sturdy but can move)
                Log.Message($"[Decompression] {furniture.def.defName} gets bed damage reduction");
            }
            else if (defName.Contains("stool") || defName.Contains("table") || defName.Contains("desk"))
            {
                damageReduction = 0.7f; // 30% less damage
                Log.Message($"[Decompression] {furniture.def.defName} gets light furniture damage reduction");
            }
            else if ((defName.Contains("plant") && defName.Contains("pot")) || defName.Contains("bonsai") || defName.Contains("planter"))
            {
                damageReduction = 0.5f; // 50% less damage (very light)
                Log.Message($"[Decompression] {furniture.def.defName} gets plant/bonsai/planter damage reduction");
            }
            else if (defName.Contains("lamp") || defName.Contains("torch") || defName.Contains("standing"))
            {
                damageReduction = 0.6f; // 40% less damage
                Log.Message($"[Decompression] {furniture.def.defName} gets lamp damage reduction");
            }
            else if (defName.Contains("chair") || defName.Contains("armchair"))
            {
                damageReduction = 0.8f; // 20% less damage
                Log.Message($"[Decompression] {furniture.def.defName} gets chair damage reduction");
            }

            // Apply damage reduction
            baseDamage *= damageReduction;

            // Displaced furniture takes additional damage
            if (wasDisplaced)
            {
                baseDamage *= 1.5f;
            }

            // Apply the damage
            if (baseDamage > 0)
            {
                DamageInfo damageInfo = new DamageInfo(
                    DamageDefOf.Blunt,
                    baseDamage,
                    0f, -1f, null, null, null,
                    DamageInfo.SourceCategory.ThingOrUnknown
                );

                furniture.TakeDamage(damageInfo);
                Log.Message($"[Decompression] Applied {baseDamage:F1} damage to {furniture.def.defName} (reduction: {damageReduction:F1}x)");
            }
        }

        private static bool TryDisplaceItem(Thing item, DecompressionEvent decompressionEvent, Map map, float severityMultiplier)
        {
            IntVec3 currentPos = item.Position;
            IntVec3 breachPoint = decompressionEvent.breachPoint;

            // Calculate target position (same logic as furniture)
            IntVec3 targetCell = FurnitureLauncher.CalculateDisplacementTarget(currentPos, breachPoint, map);

            if (targetCell == currentPos)
            {
                Log.Message($"[Decompression] No valid displacement target for item {item.def.defName}");
                return false;
            }

            // FIXED: Better displacement chance calculation for items
            float itemMass = item.def.BaseMass * item.stackCount;
            float baseDisplacementChance = decompressionEvent.severity == DecompressionSeverity.Major ? 0.95f : 0.85f;

            // Items are much easier to displace than furniture
            float massModifier;
            if (itemMass <= 0.5f)      // Very light items (clothes, medicine, etc.)
                massModifier = 1.0f;   // 100% base chance
            else if (itemMass <= 2.0f) // Light items (weapons, small stacks)
                massModifier = 0.9f;   // 90% base chance  
            else if (itemMass <= 10.0f) // Medium items (large stacks, heavy weapons)
                massModifier = 0.7f;   // 70% base chance
            else                       // Heavy items (huge stacks, very heavy things)
                massModifier = 0.4f;   // 40% base chance

            float finalChance = baseDisplacementChance * massModifier * severityMultiplier;

            // Items should have minimum 20% chance even if very heavy
            finalChance = Mathf.Max(finalChance, 0.2f);

            if (!Rand.Chance(finalChance))
            {
                Log.Message($"[Decompression] Item {item.def.defName} too heavy/stable to displace (mass: {itemMass:F1}, chance: {finalChance:F2})");
                return false;
            }


            try
            {
                // Despawn the item from current location
                item.DeSpawn(DestroyMode.Vanish);

                // Try to place it at the target location
                if (GenPlace.TryPlaceThing(item, targetCell, map, ThingPlaceMode.Near))
                {
                    // Visual effect
                    FleckMaker.ThrowDustPuffThick(currentPos.ToVector3(), map, 1.0f, Color.gray);
                    FleckMaker.ThrowDustPuffThick(targetCell.ToVector3(), map, 0.8f, Color.white);

                    Log.Message($"[Decompression] Successfully displaced item {item.def.defName} from {currentPos} to {item.Position}");
                    return true;
                }
                else
                {
                    // If placement fails, try to put it back
                    if (!GenPlace.TryPlaceThing(item, currentPos, map, ThingPlaceMode.Near))
                    {
                        Log.Error($"[Decompression] Failed to place item {item.def.defName} anywhere! Item may be lost.");
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[Decompression] Error displacing item {item.def.defName}: {ex}");
                return false;
            }
        }
        private static bool TryDisplaceFurniture(Thing furniture, DecompressionEvent decompressionEvent, Map map, float severityMultiplier)
        {
            if (furniture?.Spawned != true || furniture.Map != map)
            {
                Log.Warning($"[Decompression] Cannot displace {furniture?.def?.defName} - not properly spawned");
                return false;
            }

            // NEW: Handle items differently than buildings
            if (furniture.def.category == ThingCategory.Item)
            {
                return TryDisplaceItem(furniture, decompressionEvent, map, severityMultiplier);
            }

            if (furniture?.Spawned != true || furniture.Map != map)
            {
                Log.Warning($"[Decompression] Cannot displace {furniture?.def?.defName} - not properly spawned");
                return false;
            }

            Building building = furniture as Building;
            if (building == null)
            {
                Log.Warning($"[Decompression] {furniture.def.defName} is not a Building, cannot displace safely");
                return false;
            }

            IntVec3 currentPos = furniture.Position;
            IntVec3 breachPoint = decompressionEvent.breachPoint;

            // Calculate displacement chance based on furniture mass and decompression severity
            float furnitureMass = furniture.def.BaseMass;
            float baseDisplacementChance = decompressionEvent.severity == DecompressionSeverity.Major ? 0.8f : 0.5f;

            // Heavier furniture is less likely to move
            float massModifier = Mathf.Clamp(10f / (furnitureMass + 5f), 0.1f, 1f);
            float finalChance = baseDisplacementChance * massModifier * severityMultiplier;

            if (!Rand.Chance(finalChance))
            {
                Log.Message($"[Decompression] {furniture.def.defName} too heavy/stable to displace (mass: {furnitureMass}, chance: {finalChance:F2})");
                return false;
            }

            // Calculate target position using the launcher utility
            IntVec3 targetCell = FurnitureLauncher.CalculateDisplacementTarget(currentPos, breachPoint, map);

            if (targetCell == currentPos)
            {
                Log.Message($"[Decompression] No valid displacement target for {furniture.def.defName}");
                return false;
            }

            // Check if we should auto-rebuild (only in home zone)
            bool shouldAutoRebuild = Find.PlaySettings.autoRebuild &&
                                   map.areaManager.Home[currentPos] &&
                                   furniture.Faction == Faction.OfPlayer;

            // Perform the displacement using the projectile system
            try
            {
                // Create blueprint first if needed (before any furniture changes)
                Blueprint_Install autoRebuildBlueprint = null;
                if (shouldAutoRebuild)
                {
                    autoRebuildBlueprint = CreateAutoRebuildBlueprint(building, currentPos, map);
                }

                // Small chance the furniture falls apart during movement - adjust drop materials
                if (Rand.Chance(0.15f * severityMultiplier))
                {
                    // Get proper materials to drop based on furniture composition
                    var materialsToSpawn = GetFurnitureDropMaterials(building);

                    // Destroy the furniture
                    furniture.Destroy(DestroyMode.KillFinalize);

                    // Spawn the correct materials
                    foreach (var material in materialsToSpawn)
                    {
                        try
                        {
                            Thing materialThing;
                            if (material.stuff != null && material.thingDef.MadeFromStuff)
                            {
                                materialThing = ThingMaker.MakeThing(material.thingDef, material.stuff);
                            }
                            else
                            {
                                materialThing = ThingMaker.MakeThing(material.thingDef);
                            }
                            materialThing.stackCount = material.count;
                            GenPlace.TryPlaceThing(materialThing, targetCell.RandomAdjacentCell8Way(), map, ThingPlaceMode.Near);
                            Log.Message($"[Decompression] Spawned {material.count}x {material.thingDef.defName} (stuff: {material.stuff?.defName ?? "none"})");
                        }
                        catch (Exception ex)
                        {
                            Log.Warning($"[Decompression] Failed to create material {material.thingDef.defName} with stuff {material.stuff?.defName}: {ex.Message}");
                        }
                    }

                    if (autoRebuildBlueprint != null)
                    {
                        Log.Message($"[Decompression] {furniture.def.defName} was destroyed during displacement! Auto-rebuild blueprint created.");
                    }
                    else
                    {
                        Log.Message($"[Decompression] {furniture.def.defName} was destroyed during displacement!");
                    }
                    return true;
                }

                // Use the projectile launcher for smooth animation
                bool launchSuccess = FurnitureLauncher.LaunchFurnitureProjectile(building, targetCell, decompressionEvent);

                if (launchSuccess)
                {
                    // Visual effect at launch point
                    FleckMaker.ThrowDustPuffThick(currentPos.ToVector3(), map, 1.5f, Color.gray);

                    if (autoRebuildBlueprint != null)
                    {
                        Log.Message($"[Decompression] Successfully launched {furniture.def.defName} from {currentPos} to {targetCell}. Auto-rebuild blueprint created.");
                    }
                    else
                    {
                        Log.Message($"[Decompression] Successfully launched {furniture.def.defName} from {currentPos} to {targetCell}");
                    }
                    return true;
                }
                else
                {
                    Log.Warning($"[Decompression] Failed to launch {furniture.def.defName} - falling back to instant movement");
                    // Fallback to the original minification system if projectile fails
                    return TryDisplaceFurnitureInstant(building, targetCell, map);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[Decompression] Critical error displacing furniture {furniture?.def?.defName}: {ex}");
                return false;
            }
        }

        private static Blueprint_Install CreateAutoRebuildBlueprint(Building building, IntVec3 originalPos, Map map)
        {
            try
            {
                // Only create blueprint if building can be reinstalled
                if (building.def.Minifiable && building.def.installBlueprintDef != null)
                {
                    return GenConstruct.PlaceBlueprintForReinstall(building, originalPos, map, building.Rotation, Faction.OfPlayer, false);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[Decompression] Failed to create auto-rebuild blueprint for {building.def.defName}: {ex}");
            }
            return null;
        }

        private static List<MaterialDropInfo> GetFurnitureDropMaterials(Building building)
        {
            var materials = new List<MaterialDropInfo>();

            try
            {
                // Use RimWorld's cost calculation system to get proper materials
                var costList = building.def.CostListAdjusted(building.Stuff, true);

                foreach (var cost in costList)
                {
                    // Use the same calculation as when building is destroyed normally
                    int dropAmount = GenMath.RoundRandom(cost.count * 0.25f); // Same as DestroyMode.KillFinalize

                    if (dropAmount > 0)
                    {
                        // Only pass stuff if the thing is actually made from stuff
                        ThingDef stuffToUse = (cost.thingDef.MadeFromStuff && building.Stuff != null) ? building.Stuff : null;

                        materials.Add(new MaterialDropInfo
                        {
                            thingDef = cost.thingDef,
                            stuff = stuffToUse,
                            count = dropAmount
                        });

                        Log.Message($"[Decompression] Material: {cost.thingDef.defName}, MadeFromStuff: {cost.thingDef.MadeFromStuff}, stuff: {stuffToUse?.defName ?? "none"}");
                    }
                }

                // If no materials from cost list, add some basic materials based on stuff
                if (materials.Count == 0 && building.Stuff != null)
                {
                    int basicAmount = Mathf.Max(1, Mathf.RoundToInt(building.def.VolumePerUnit * 0.1f));

                    // Create the stuff material directly (like wood logs, steel, etc.)
                    materials.Add(new MaterialDropInfo
                    {
                        thingDef = building.Stuff,
                        stuff = null, // The stuff itself doesn't need stuff
                        count = basicAmount
                    });
                }

                // Ultimate fallback: some generic chunks
                if (materials.Count == 0)
                {
                    materials.Add(new MaterialDropInfo
                    {
                        thingDef = ThingDefOf.ChunkSlagSteel,
                        stuff = null,
                        count = 1
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[Decompression] Error calculating drop materials for {building.def.defName}: {ex}");

                // Fallback: drop some steel chunks
                materials.Add(new MaterialDropInfo
                {
                    thingDef = ThingDefOf.ChunkSlagSteel,
                    stuff = null,
                    count = 1
                });
            }

            return materials;
        }

        private struct MaterialDropInfo
        {
            public ThingDef thingDef;
            public ThingDef stuff;
            public int count;
        }

        // Fallback method for instant displacement if projectile system fails
        private static bool TryDisplaceFurnitureInstant(Building building, IntVec3 targetCell, Map map)
        {
            try
            {
                // Use RimWorld's minification system to preserve ALL furniture data
                MinifiedThing minifiedThing = building.MakeMinified();
                if (minifiedThing == null)
                {
                    Log.Warning($"[Decompression] Failed to minify {building.def.defName}");
                    return false;
                }

                // The original building is automatically destroyed when minified
                Thing reinstalledThing = minifiedThing.GetInnerIfMinified();
                Building reinstalledBuilding = reinstalledThing as Building;

                if (reinstalledBuilding == null)
                {
                    Log.Error($"[Decompression] Error getting building from minified thing");
                    // Try to place the minified thing back somewhere safe
                    GenPlace.TryPlaceThing(minifiedThing, targetCell, map, ThingPlaceMode.Near);
                    return false;
                }

                // Install the building at the target location
                if (GenConstruct.CanPlaceBlueprintAt(reinstalledBuilding.def, targetCell, reinstalledBuilding.Rotation, map).Accepted)
                {
                    GenSpawn.Spawn(reinstalledBuilding, targetCell, map, reinstalledBuilding.Rotation);
                    minifiedThing.Destroy(DestroyMode.Vanish);

                    // Visual effect
                    FleckMaker.ThrowDustPuffThick(targetCell.ToVector3(), map, 1.0f, Color.white);
                    Log.Message($"[Decompression] Instantly moved {building.def.defName} to {targetCell}");
                    return true;
                }
                else
                {
                    // If we can't place at target, drop the minified thing nearby
                    Log.Warning($"[Decompression] Cannot place {building.def.defName} at {targetCell}, placing minified version nearby");
                    GenPlace.TryPlaceThing(minifiedThing, targetCell, map, ThingPlaceMode.Near);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[Decompression] Error in instant furniture displacement: {ex}");
                return false;
            }
        }
        private static bool IsValidFurniturePosition(IntVec3 cell, Thing furniture, Map map)
        {
            if (!cell.InBounds(map))
            {
                Log.Message($"[Decompression] {cell} invalid - out of bounds");
                return false;
            }

            if (!cell.Standable(map))
            {
                Log.Message($"[Decompression] {cell} invalid - not standable");
                return false;
            }

            // Check if there's room for this furniture
            CellRect occupiedRect = CellRect.CenteredOn(cell, furniture.def.size.x, furniture.def.size.z);
            if (!occupiedRect.InBounds(map))
            {
                Log.Message($"[Decompression] {cell} invalid - furniture rect out of bounds");
                return false;
            }

            // Check for blocking things - be more strict to prevent collisions
            foreach (var occupiedCell in occupiedRect)
            {
                var things = occupiedCell.GetThingList(map);
                foreach (var thing in things)
                {
                    // Skip the furniture we're trying to move
                    if (thing == furniture) continue;

                    // Block if there's ANY other building (this prevents furniture collisions)
                    if (thing is Building)
                    {
                        Log.Message($"[Decompression] {cell} invalid - blocked by building {thing.def.defName}");
                        return false;
                    }

                    // Block if there's a pawn
                    if (thing is Pawn)
                    {
                        Log.Message($"[Decompression] {cell} invalid - blocked by pawn {thing.Label}");
                        return false;
                    }

                    // Block only truly impassable things
                    if (thing.def.passability == Traversability.Impassable)
                    {
                        Log.Message($"[Decompression] {cell} invalid - blocked by impassable {thing.def.defName}");
                        return false;
                    }
                }
            }

            Log.Message($"[Decompression] {cell} is VALID for {furniture.def.defName}");
            return true;
        }
        private static bool IsDisplaceableItem(Thing thing)
        {
            // Must be an item category
            if (thing.def.category != ThingCategory.Item) return false;

            // Skip things that are being carried
            if (thing.ParentHolder is Pawn_CarryTracker) return false;

            // Skip quest items or other special items
            if (thing.questTags?.Any() == true) return false;

            // ALWAYS displace very light items regardless of mass threshold
            if (thing.def.BaseMass <= 0.1f)
            {
                Log.Message($"[Decompression] {thing.def.defName} is very light - always displaceable!");
                return true;
            }

            // Skip things that are too heavy
            if (thing.def.BaseMass > 75f) return false;

            Log.Message($"[Decompression] {thing.def.defName} PASSED item displacement check!");
            return true;
        }

        private static void ApplyDecompressionToPawn(Pawn pawn, DecompressionEvent decompressionEvent, Map map)
        {
            if (!pawn.RaceProps.IsFlesh || pawn.Dead) return;

            // Get vacuum resistance using vanilla stat system
            float vacuumResistance = 0f;
            try
            {
                // Use the vanilla StatDefOf directly like the game does
                vacuumResistance = pawn.GetStatValue(StatDefOf.VacuumResistance);
                Log.Message($"[Decompression] {pawn.Name.ToStringShort} vacuum resistance: {vacuumResistance:F3}");
            }
            catch (Exception ex)
            {
                Log.Warning($"[Decompression] Error getting vacuum resistance: {ex}");

                // Fallback: manually calculate from apparel like vanilla VacuumUtility does
                if (pawn.apparel?.WornApparel != null)
                {
                    foreach (var apparel in pawn.apparel.WornApparel)
                    {
                        float apparelResistance = apparel.GetStatValue(StatDefOf.VacuumResistance);
                        vacuumResistance += apparelResistance;
                        Log.Message($"[Decompression] {apparel.def.defName} adds {apparelResistance:F3} resistance");
                    }
                }
            }

            float protectionFactor = Mathf.Clamp01(vacuumResistance);

            // Calculate base severity - MUCH MORE AGGRESSIVE for naked pawns
            float baseSeverity = 0.25f;
            switch (decompressionEvent.severity)
            {
                case DecompressionSeverity.Major: baseSeverity = 0.95f; break; // Very high for naked
                case DecompressionSeverity.Minor: baseSeverity = 0.70f; break; // High for naked  
            }

            // Apply protection - naked pawns (0% resistance) get full damage
            // Well-protected pawns (80%+ resistance) get much less
            float finalSeverity = baseSeverity * (1f - protectionFactor * 0.85f); // Max 85% protection

            // Add randomization (±10% variation)
            float randomFactor = Rand.Range(0.90f, 1.10f);
            finalSeverity *= randomFactor;

            finalSeverity = Mathf.Max(finalSeverity, 0.05f); // Minimum 5% severity
            finalSeverity = Mathf.Min(finalSeverity, 0.95f); // Cap at 95%

            Log.Message($"[Decompression] {pawn.Name.ToStringShort}: resistance={vacuumResistance:F3}, protection={protectionFactor:F3}, base={baseSeverity:F3}, final={finalSeverity:F3}");

            // Apply hediff
            HediffDef hediffToUse = HediffDefOf.VacuumExposure;
            var customHediff = DefDatabase<HediffDef>.GetNamedSilentFail("DecompressionInjury");
            if (customHediff != null) hediffToUse = customHediff;

            var hediff = HediffMaker.MakeHediff(hediffToUse, pawn);
            if (hediff != null)
            {
                // Apply the FULL severity for health effects - this determines injury severity
                hediff.Severity = finalSeverity; // Use the calculated severity directly!
                pawn.health.AddHediff(hediff);

                Log.Message($"[Decompression] Applied hediff with FULL severity {finalSeverity:F3} to {pawn.Name.ToStringShort}");
            }

            // Try displacement
            bool displaced = TryDisplacePawn(pawn, decompressionEvent, map);

            // Apply stunning
            ApplyStunning(pawn, decompressionEvent.severity, finalSeverity, displaced);

            // Apply additional injuries
            Pawn_Injury_Utility.ApplyAdditionalInjuries(pawn, finalSeverity, decompressionEvent.severity);

            Log.Message($"[Decompression] Applied effects to {pawn.Name.ToStringShort} (severity: {finalSeverity:F2}, displaced: {displaced})");
        }

        private static bool TryDisplacePawn(Pawn pawn, DecompressionEvent decompressionEvent, Map map)
        {
            IntVec3 currentPos = pawn.Position;
            IntVec3 breachPoint = decompressionEvent.breachPoint;
            IntVec3 directionVector = (breachPoint - currentPos);

            if (directionVector.LengthHorizontalSquared == 0) return false;

            IntVec3 targetCell = currentPos;

            if (Mathf.Abs(directionVector.x) > Mathf.Abs(directionVector.z))
            {
                targetCell.x += directionVector.x > 0 ? 1 : -1;
            }
            else if (directionVector.z != 0)
            {
                targetCell.z += directionVector.z > 0 ? 1 : -1;
            }

            if (targetCell.InBounds(map) && targetCell.Walkable(map) &&
                !targetCell.Impassable(map) && targetCell.GetFirstPawn(map) == null &&
                !targetCell.GetThingList(map).Any(t => t.def.passability == Traversability.Impassable))
            {
                pawn.Position = targetCell;
                pawn.Notify_Teleported(false, false);

                try
                {
                    if (pawn.jobs != null)
                    {
                        pawn.jobs.EndCurrentJob(Verse.AI.JobCondition.InterruptForced, true);
                        pawn.jobs.ClearQueuedJobs();
                        pawn.jobs.StopAll(false); // Additional job clearing
                    }
                    if (pawn.pather != null)
                    {
                        pawn.pather.StopDead();
                        pawn.pather.ResetToCurrentPosition(); // Additional path reset
                    }
                    if (pawn.Drawer?.tweener != null)
                    {
                        pawn.Drawer.tweener.ResetTweenedPosToRoot();
                    }
                    // Force immediate AI refresh
                    if (pawn.mindState != null && pawn.mindState.mentalStateHandler != null)
                    {
                        pawn.mindState.mentalStateHandler.Reset();
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[Decompression] Error resetting AI: {ex}");
                }

                FleckMaker.ThrowDustPuffThick(currentPos.ToVector3(), map, 2f, Color.white);
                Log.Message($"[Decompression] Displaced {pawn.Name.ToStringShort} towards breach");
                return true;
            }

            return false;
        }

        private static void ApplyStunning(Pawn pawn, DecompressionSeverity severity, float hediffSeverity, bool wasDisplaced)
        {
            float stunDamageAmount = 0f;
            switch (severity)
            {
                case DecompressionSeverity.Major: stunDamageAmount = 8f; break;
                case DecompressionSeverity.Minor: stunDamageAmount = 4f; break;
            }

            DamageInfo stunDamage = new DamageInfo(
                DamageDefOf.Stun,
                stunDamageAmount,
                0f, -1f, null, null, null,
                DamageInfo.SourceCategory.ThingOrUnknown
            );

            var damageResult = pawn.TakeDamage(stunDamage);

            Log.Message($"[Decompression] Applied {stunDamageAmount} stun damage, result stunned: {damageResult.stunned}");

            if (!wasDisplaced && hediffSeverity > 0.5f && Rand.Chance(0.4f))
            {
                HealthUtility.DamageUntilDowned(pawn, false);
            }
        }
    }
}