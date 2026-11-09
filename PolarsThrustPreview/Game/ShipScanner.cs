using System.Collections.Generic;
using PolarsThrustPreview.Core;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace PolarsThrustPreview.Game;

/// <summary>
/// Turns a grid and its mechanical subgrids (rotors, pistons, hinges) into a <see cref="ShipModel"/>.
/// The block lists are scanned only when the ship changes (block added or removed, a subgrid
/// attached or lost) and every 10 seconds as a safety net. The cheap live values (fuel,
/// batteries, cargo, thruster directions) are refreshed from the cached lists.
/// </summary>
internal sealed class ShipScanner
{
    private sealed class ThrusterEntry
    {
        public IMyThrust Block;
        public MyThrustDefinition Definition;
        public ThrusterSpec Spec;
        public float LastThrustMultiplier = float.NaN;
        public float LastPowerMultiplier = float.NaN;
    }

    private struct InventoryEntry
    {
        public IMyInventory Inventory;
    }

    private struct TankEntry
    {
        public IMyGasTank Block;
        public int Fuel;
    }

    private const int RescanTicks = 600;

    public readonly ShipModel Model = new();

    /// <summary>Grids in the scanned group, root first.</summary>
    public readonly List<IMyCubeGrid> Grids = new();

    public IMyCubeGrid Root { get; private set; }

    /// <summary>Best reference block when nobody is seated: main cockpit, then any controller.</summary>
    public IMyShipController DefaultController { get; private set; }

    public bool AnyStatic { get; private set; }

    private readonly List<ThrusterEntry> thrusters = new();
    private readonly List<InventoryEntry> inventories = new();
    private readonly List<TankEntry> tanks = new();
    private readonly List<IMyBatteryBlock> batteries = new();
    private readonly List<IMyPowerProducer> generators = new();
    private readonly List<CargoSlot> scannedCargo = new();
    private readonly Dictionary<MyDefinitionId, int> fuelIndex = new();
    private readonly List<IMySlimBlock> blockBuffer = new();
    private readonly List<IMyCubeGrid> groupBuffer = new();
    private double scannedBaseMass;
    private bool dirty = true;
    private int ticksSinceScan;

    public void Attach(IMyCubeGrid root)
    {
        if (root == Root)
            return;
        Detach();
        Root = root;
        dirty = true;
    }

    public void Detach()
    {
        foreach (var grid in Grids)
        {
            grid.OnBlockAdded -= OnBlockChanged;
            grid.OnBlockRemoved -= OnBlockChanged;
        }
        Grids.Clear();
        thrusters.Clear();
        inventories.Clear();
        tanks.Clear();
        batteries.Clear();
        generators.Clear();
        scannedCargo.Clear();
        fuelIndex.Clear();
        Model.Thrusters.Clear();
        Model.Fuels.Clear();
        Model.Cargo.Clear();
        DefaultController = null;
        Root = null;
        dirty = true;
    }

    private void OnBlockChanged(IMySlimBlock block) => dirty = true;

    /// <summary>Rescans when needed. Call about once a second while the panel is shown.</summary>
    public void Update(int ticksElapsed)
    {
        if (Root == null)
            return;

        ticksSinceScan += ticksElapsed;
        if (!dirty && ticksSinceScan < RescanTicks && !GroupChanged())
            return;

        Scan();
    }

    private bool GroupChanged()
    {
        groupBuffer.Clear();
        MyAPIGateway.GridGroups.GetGroup(Root, GridLinkTypeEnum.Mechanical, groupBuffer);
        if (groupBuffer.Count != Grids.Count)
            return true;
        foreach (var grid in groupBuffer)
            if (!Grids.Contains(grid))
                return true;
        return false;
    }

    private void Scan()
    {
        var root = Root;
        Detach();
        Root = root;
        dirty = false;
        ticksSinceScan = 0;

        if (root.Closed)
        {
            Root = null;
            return;
        }

        groupBuffer.Clear();
        MyAPIGateway.GridGroups.GetGroup(root, GridLinkTypeEnum.Mechanical, groupBuffer);
        if (!groupBuffer.Contains(root))
            groupBuffer.Insert(0, root);

        scannedBaseMass = 0;
        AnyStatic = false;
        IMyShipController anyController = null;

        foreach (var grid in groupBuffer)
        {
            if (grid == null || grid.Closed)
                continue;

            Grids.Add(grid);
            grid.OnBlockAdded += OnBlockChanged;
            grid.OnBlockRemoved += OnBlockChanged;
            AnyStatic |= grid.IsStatic;

            blockBuffer.Clear();
            grid.GetBlocks(blockBuffer);
            foreach (var slim in blockBuffer)
            {
                scannedBaseMass += slim.Mass;
                var block = slim.FatBlock;
                if (block == null)
                    continue;

                AddInventories(block);

                switch (block)
                {
                    case IMyThrust thrust:
                        AddThruster(thrust);
                        break;
                    case IMyGasTank tank:
                        AddTank(tank);
                        break;
                    case IMyBatteryBlock battery:
                        batteries.Add(battery);
                        break;
                    case IMyPowerProducer producer:
                        generators.Add(producer);
                        break;
                    case IMyShipController controller:
                        if (controller.IsMainCockpit)
                            DefaultController = controller;
                        else if (anyController == null && controller.CanControlShip)
                            anyController = controller;
                        break;
                }
            }
        }

        DefaultController ??= anyController;
        blockBuffer.Clear();
    }

    private void AddInventories(IMyCubeBlock block)
    {
        if (!block.HasInventory)
            return;

        for (int i = 0; i < block.InventoryCount; i++)
        {
            var inventory = block.GetInventory(i);
            if (inventory == null)
                continue;

            double volume = (double)inventory.MaxVolume;
            if (volume <= 0)
                continue;

            var (heavy, max) = ItemDensity.For(inventory, block.BlockDefinition, i);
            inventories.Add(new InventoryEntry { Inventory = inventory });
            scannedCargo.Add(new CargoSlot { VolumeM3 = volume, HeavyDensity = heavy, MaxDensity = max });
        }
    }

    private void AddThruster(IMyThrust thrust)
    {
        if (!(MyDefinitionManager.Static.GetCubeBlockDefinition(thrust.BlockDefinition) is MyThrustDefinition def))
            return;

        var spec = SpecFor(def, out int fuel);
        spec.Fuel = fuel;
        thrusters.Add(new ThrusterEntry { Block = thrust, Definition = def, Spec = spec });
    }

    /// <summary>Thruster numbers from its definition, without the per-block multipliers.</summary>
    public ThrusterSpec SpecFor(MyThrustDefinition def, out int fuel)
    {
        fuel = -1;
        double litresPerMWs = 0;

        var converter = def.FuelConverter;
        if (converter != null && !converter.FuelId.IsNull())
        {
            MyDefinitionId fuelId = converter.FuelId;
            if (MyDefinitionManager.Static.TryGetDefinition(fuelId, out MyGasProperties gas) && gas.EnergyDensity > 0)
            {
                fuel = FuelIndex(fuelId, gas.DisplayNameText ?? fuelId.SubtypeName);
                float efficiency = converter.Efficiency > 0 ? converter.Efficiency : 1f;
                litresPerMWs = 1.0 / (efficiency * gas.EnergyDensity);
            }
        }

        return new ThrusterSpec
        {
            Force = def.ForceMagnitude,
            MaxPowerMW = def.MaxPowerConsumption,
            MinInfluence = def.MinPlanetaryInfluence,
            MaxInfluence = def.MaxPlanetaryInfluence,
            EffectivenessAtMin = def.EffectivenessAtMinInfluence,
            EffectivenessAtMax = def.EffectivenessAtMaxInfluence,
            NeedsAtmosphere = def.NeedsAtmosphereForInfluence,
            Fuel = fuel,
            LitresPerMWs = litresPerMWs,
        };
    }

    private int FuelIndex(MyDefinitionId fuelId, string name)
    {
        if (fuelIndex.TryGetValue(fuelId, out int index))
            return index;
        index = Model.Fuels.Count;
        fuelIndex[fuelId] = index;
        Model.Fuels.Add(new FuelPool { Name = name });
        return index;
    }

    private void AddTank(IMyGasTank tank)
    {
        if (!(MyDefinitionManager.Static.GetCubeBlockDefinition(tank.BlockDefinition) is MyGasTankDefinition def))
            return;

        // Oxygen and hydrogen tanks share a TypeId, the stored gas tells them apart.
        string name = def.StoredGasId.SubtypeName;
        if (MyDefinitionManager.Static.TryGetDefinition(def.StoredGasId, out MyGasProperties gas))
            name = gas.DisplayNameText ?? name;
        tanks.Add(new TankEntry { Block = tank, Fuel = FuelIndex(def.StoredGasId, name) });
    }

    /// <summary>
    /// Fills the live values: thruster directions in the reference frame, working state,
    /// fuel, batteries, generators, cargo and mass.
    /// </summary>
    public void Refresh(MatrixD reference, double worldCargoMassFactor, double cargoMassFactor, double volumeScale, double thrustScale)
    {
        var model = Model;

        // Thrusters (scanned ones only; build-mode extras are appended by the caller)
        model.Thrusters.Clear();
        foreach (var entry in thrusters)
        {
            float tm = entry.Block.ThrustMultiplier, pm = entry.Block.PowerConsumptionMultiplier;
            if (tm != entry.LastThrustMultiplier || pm != entry.LastPowerMultiplier)
            {
                entry.Spec.Force = entry.Definition.ForceMagnitude * tm;
                entry.Spec.MaxPowerMW = entry.Definition.MaxPowerConsumption * pm;
                entry.LastThrustMultiplier = tm;
                entry.LastPowerMultiplier = pm;
            }
            model.Thrusters.Add(entry.Spec);
        }
        model.EnsureCapacity();

        var toReference = MatrixD.Transpose(reference);
        for (int i = 0; i < thrusters.Count; i++)
        {
            var block = thrusters[i].Block;
            model.Working[i] = block.IsFunctional && !block.Closed;

            // The game pushes a ship along the thruster's Backward (MyThrust.ThrustForce = -Forward).
            var push = Vector3D.TransformNormal(block.WorldMatrix.Backward, toReference);
            model.PushDirections[i * 3] = push.X;
            model.PushDirections[i * 3 + 1] = push.Y;
            model.PushDirections[i * 3 + 2] = push.Z;
        }

        // Fuel
        foreach (var pool in model.Fuels)
            pool.StoredLitres = pool.CapacityLitres = 0;
        foreach (var tank in tanks)
        {
            if (tank.Block.Closed || !tank.Block.IsFunctional)
                continue;
            var pool = model.Fuels[tank.Fuel];
            pool.CapacityLitres += tank.Block.Capacity;
            pool.StoredLitres += tank.Block.Capacity * tank.Block.FilledRatio;
        }

        // Power
        model.BatteryStoredMWh = model.BatteryCapacityMWh = 0;
        foreach (var battery in batteries)
        {
            if (battery.Closed || !battery.IsFunctional)
                continue;
            model.BatteryStoredMWh += battery.CurrentStoredPower;
            model.BatteryCapacityMWh += battery.MaxStoredPower;
        }
        model.GeneratorMW = 0;
        foreach (var generator in generators)
        {
            if (generator.Closed || !generator.IsWorking)
                continue;
            model.GeneratorMW += generator.MaxOutput;
        }

        // Cargo and mass
        model.Cargo.Clear();
        model.Cargo.AddRange(scannedCargo);
        double cargoMass = 0;
        foreach (var entry in inventories)
            cargoMass += (double)entry.Inventory.CurrentMass;
        model.CargoMass = cargoMass;
        model.BaseMass = scannedBaseMass;
        model.WorldCargoMassFactor = worldCargoMassFactor;
        model.CargoMassFactor = cargoMassFactor;
        model.VolumeScale = volumeScale;
        model.ThrustScale = thrustScale;

        model.PhysicalMass = double.NaN;
        if (!AnyStatic)
        {
            double physical = 0;
            foreach (var grid in Grids)
            {
                var physics = grid.Physics;
                if (grid.Closed || physics == null || physics.Mass <= 0)
                {
                    physical = double.NaN;
                    break;
                }
                physical += physics.Mass;
            }
            model.PhysicalMass = physical;
        }
    }
}
