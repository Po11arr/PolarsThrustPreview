using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.Game;
using VRage.Game;
using VRage.Game.ModAPI;

namespace PolarsThrustPreview.Game;

/// <summary>
/// Densest items an inventory accepts, kg/m3: any item ("max") and ores only ("heavy",
/// falling back to any item when the inventory takes no ore). Item list read from the loaded
/// definitions, so modded items count. Cached per block type and inventory index.
/// </summary>
internal static class ItemDensity
{
    private struct Item
    {
        public MyDefinitionId Id;
        public double Density;
        public bool IsOre;
    }

    private static readonly List<Item> items = new();
    private static readonly Dictionary<(MyDefinitionId, int), (double heavy, double max)> cache = new();
    private static bool loaded;

    private static double densestAny;
    private static double densestOre;

    public static void Reset()
    {
        items.Clear();
        cache.Clear();
        loaded = false;
        densestAny = densestOre = 0;
    }

    private static void Load()
    {
        loaded = true;
        foreach (var def in MyDefinitionManager.Static.GetPhysicalItemDefinitions())
        {
            if (def == null || !def.Public || def.Mass <= 0 || def.Volume <= 0)
                continue;

            var item = new Item
            {
                Id = def.Id,
                Density = def.Mass / def.Volume,
                IsOre = def.Id.TypeId.ToString() == "MyObjectBuilder_Ore",
            };
            items.Add(item);

            if (item.Density > densestAny) densestAny = item.Density;
            if (item.IsOre && item.Density > densestOre) densestOre = item.Density;
        }

        if (densestOre <= 0)
            densestOre = densestAny;
    }

    public static (double heavy, double max) For(IMyInventory inventory, MyDefinitionId blockId, int index)
    {
        if (!loaded)
            Load();

        var key = (blockId, index);
        if (cache.TryGetValue(key, out var known))
            return known;

        var constraint = (inventory as MyInventory)?.Constraint;
        double ore = 0, any = 0;
        foreach (var item in items)
        {
            if (constraint != null && !constraint.Check(item.Id))
                continue;
            if (item.Density > any) any = item.Density;
            if (item.IsOre && item.Density > ore) ore = item.Density;
        }

        var result = (ore > 0 ? ore : any, any);
        cache[key] = result;
        return result;
    }

    /// <summary>Densities for a block that is not placed yet (cargo containers take anything).</summary>
    public static (double heavy, double max) Unconstrained()
    {
        if (!loaded)
            Load();
        return (densestOre, densestAny);
    }
}
