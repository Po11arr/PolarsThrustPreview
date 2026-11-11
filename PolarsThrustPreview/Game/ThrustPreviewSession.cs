using System;
using System.Collections.Generic;
using PolarsThrustPreview.Core;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Input;
using VRageMath;

namespace PolarsThrustPreview.Game;

/// <summary>
/// Client-side session component. Decides what to show (cockpit panel, build preview or
/// nothing) ten times a second and refreshes the numbers once a second. While building it
/// also refreshes at once when the held block, its orientation, its symmetry copies, the
/// target or the ship's blocks change. Every frame it only draws the prepared layout.
/// </summary>
[MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
public class ThrustPreviewSession : MySessionComponentBase
{
    private enum Mode
    {
        None,
        Cockpit,
        Build,
    }

    private const int DecideTicks = 6;
    private const int CockpitRefreshTicks = 60;

    private readonly ShipScanner scanner = new();
    private readonly Report report = new();
    private readonly List<DrawOp> ops = new();
    private float panelHeight;
    private readonly List<MatrixD> placements = new();
    private readonly double[] pushBins = new double[6];
    private readonly List<IMyCubeGrid> physicalGroup = new();

    private Mode mode;
    /// <summary>The panel key while seated cycles: off, full, collapsed.</summary>
    private int cockpitState;

    /// <summary>The panel key while holding a block cycles: full, collapsed, hidden. Starts on full.</summary>
    private int buildState;

    /// <summary>True while a block is held over a grid, even when the build panel is hidden.</summary>
    private bool holdingBlock;
    private bool failed;
    private int tick;
    private int lastRefreshTick;
    private MyDefinitionId lastHeldId;
    private int lastHeldCount = -1;
    private readonly List<Vector3D> lastPushes = new();

    /// <summary>Cargo mass share confirmed in a cockpit with real cargo, or NaN until then.</summary>
    private double measuredCargoFactor = double.NaN;

    public override void LoadData()
    {
        try
        {
            ItemDensity.Reset();
            BuilderAccess.Reset();
        }
        catch (Exception e)
        {
            Fail("Load failed", e);
        }
    }

    protected override void UnloadData()
    {
        try
        {
            scanner.Detach();
            ops.Clear();
            ItemDensity.Reset();
            BuilderAccess.Reset();
        }
        catch (Exception e)
        {
            Log.Error("Unload failed", e);
        }
    }

    public override void HandleInput()
    {
        if (failed)
            return;
        try
        {
            var config = Config.Current;
            if (!config.Enabled)
                return;

            var gui = MyAPIGateway.Gui;
            if (gui == null || gui.IsCursorVisible || gui.ChatEntryVisible)
                return;

            bool seated = MyAPIGateway.Session?.ControlledObject is IMyShipController;
            if (!holdingBlock && !seated)
                return;

            if (config.ToggleKey.HasPressed(MyInput.Static))
            {
                string text;
                if (holdingBlock)
                {
                    buildState = (buildState + 1) % 3;
                    text = buildState == 0 ? "Thrust Preview: full" : buildState == 1 ? "Thrust Preview: collapsed" : "Thrust Preview: hidden while building";
                }
                else
                {
                    cockpitState = (cockpitState + 1) % 3;
                    text = cockpitState == 1 ? "Thrust Preview: full" : cockpitState == 2 ? "Thrust Preview: collapsed" : "Thrust Preview: off";
                }
                MyAPIGateway.Utilities.ShowNotification(text, 1500);
                lastRefreshTick = int.MinValue / 2;
            }
        }
        catch (Exception e)
        {
            Fail("Input handling failed", e);
        }
    }

    public override void UpdateAfterSimulation()
    {
        if (failed)
            return;
        try
        {
            tick++;
            if (tick % DecideTicks != 0)
                return;

            var config = Config.Current;
            if (!config.Enabled)
            {
                holdingBlock = false;
                Hide();
                return;
            }

            // Decide what to show
            MyCubeBlockDefinition held = null;
            IMyCubeGrid root = null;
            IMyShipController controller = null;
            var newMode = Mode.None;

            IMyCubeGrid target = null;
            holdingBlock = config.ShowWhileBuilding && BuilderAccess.TryGetHeld(out held, out target, placements) && CanBuildOn(target);
            if (holdingBlock)
            {
                if (buildState == 2)
                {
                    Hide();
                    return;
                }
                newMode = Mode.Build;
                root = target;
            }
            else if (cockpitState != 0 && MyAPIGateway.Session?.ControlledObject is IMyShipController seat && seat.CubeGrid != null)
            {
                newMode = Mode.Cockpit;
                controller = seat;
                root = seat.CubeGrid;
            }

            if (newMode == Mode.None || root == null)
            {
                Hide();
                return;
            }

            // Both trackers always run so their stored state stays current
            bool heldChanged = HeldChanged(held);
            bool pushesChanged = PushesChanged();
            bool changed = newMode != mode || root != scanner.Root || heldChanged || pushesChanged || scanner.Dirty;
            mode = newMode;
            scanner.Attach(root);

            if (!changed && tick - lastRefreshTick < CockpitRefreshTicks)
                return;

            Refresh(config, controller, held);
        }
        catch (Exception e)
        {
            Fail("Update failed", e);
        }
    }

    public override void Draw()
    {
        if (failed || mode == Mode.None || ops.Count == 0)
            return;
        try
        {
            if (MyAPIGateway.Session?.Config != null && MyAPIGateway.Session.Config.HudState == 0)
                return;

            var config = Config.Current;
            PanelDraw.Draw(ops, panelHeight, config.TextScale, config.PositionX, config.PositionY);
        }
        catch (Exception e)
        {
            Fail("Drawing failed", e);
        }
    }

    private bool HeldChanged(MyCubeBlockDefinition held)
    {
        var id = held?.Id ?? default;
        int count = held == null ? -1 : Math.Max(1, placements.Count);
        bool changed = id != lastHeldId || count != lastHeldCount;
        lastHeldId = id;
        lastHeldCount = count;
        return changed;
    }

    /// <summary>
    /// Build mode only previews grids the player could open in the terminal: unowned, owned by
    /// the player, or owned by the player's faction. Other players' ships stay hidden.
    /// </summary>
    private static bool CanBuildOn(IMyCubeGrid grid)
    {
        if (grid == null)
            return false;
        var owners = grid.BigOwners;
        if (owners == null || owners.Count == 0)
            return true;

        long me = MyAPIGateway.Session?.Player?.IdentityId ?? 0;
        if (me == 0)
            return false;
        var factions = MyAPIGateway.Session.Factions;
        var myFaction = factions?.TryGetPlayerFaction(me);
        foreach (var owner in owners)
        {
            if (owner == me)
                return true;
            if (myFaction != null && factions.TryGetPlayerFaction(owner) == myFaction)
                return true;
        }
        return false;
    }

    /// <summary>
    /// True when a placement's push direction changed (rotation or symmetry). Moving the
    /// block across the grid changes nothing on the panel, so position is ignored.
    /// </summary>
    private bool PushesChanged()
    {
        bool changed = placements.Count != lastPushes.Count;
        if (!changed)
        {
            for (int i = 0; i < placements.Count; i++)
            {
                if (Vector3D.DistanceSquared(placements[i].Backward, lastPushes[i]) > 1e-6)
                {
                    changed = true;
                    break;
                }
            }
        }

        if (changed)
        {
            lastPushes.Clear();
            for (int i = 0; i < placements.Count; i++)
                lastPushes.Add(placements[i].Backward);
        }
        return changed;
    }

    private void Refresh(Config config, IMyShipController controller, MyCubeBlockDefinition held)
    {
        lastRefreshTick = tick;

        scanner.Update();
        var root = scanner.Root;
        if (root == null)
        {
            Hide();
            return;
        }

        // Inventory size and the share of cargo mass physics counts
        var settings = MyAPIGateway.Session.SessionSettings;
        double blocksMultiplier = settings != null && settings.BlocksInventorySizeMultiplier > 0 ? settings.BlocksInventorySizeMultiplier : 1.0;
        double characterMultiplier = settings != null && settings.InventorySizeMultiplier > 0 ? settings.InventorySizeMultiplier : 1.0;
        double worldFactor = double.IsNaN(measuredCargoFactor) ? 1.0 / blocksMultiplier : measuredCargoFactor;
        bool overridden = config.InventoryMultiplier > 0;
        double chosen = overridden ? config.InventoryMultiplier : blocksMultiplier;
        double volumeScale = chosen / blocksMultiplier;
        double cargoFactor = ThrustMath.CargoMassFactor(worldFactor, blocksMultiplier, chosen);

        // Reference frame: the seat in use, else the main cockpit, else the grid
        var referenceBlock = controller ?? scanner.DefaultController;
        MatrixD reference = referenceBlock?.WorldMatrix ?? root.WorldMatrix;

        scanner.Refresh(reference, worldFactor, cargoFactor, volumeScale, config.ThrustMultiplier);
        var model = scanner.Model;

        if (controller != null)
            MeasureCargoFactor(controller, model, characterMultiplier, blocksMultiplier);

        var context = new PanelContext
        {
            ShipName = root.CustomName,
            Building = mode == Mode.Build,
            ChosenInventoryMultiplier = chosen,
            InventoryOverridden = overridden,
            ThrustMultiplier = config.ThrustMultiplier,
            StaticGrid = scanner.AnyStatic,
        };

        if (mode == Mode.Build && held != null)
            AddHeldBlock(held, reference, blocksMultiplier, ref context);

        var position = referenceBlock?.GetPosition() ?? root.WorldAABB.Center;
        report.Compute(model, Ambient.At(position));
        bool collapsed = mode == Mode.Build ? buildState == 1 : cockpitState == 2;
        var view = collapsed ? PanelView.Collapsed : PanelView.Full;
        panelHeight = PanelLayout.Build(report, context, view, ops);
    }

    /// <summary>Adds the block in hand (and its symmetry copies) to the model before it is placed.</summary>
    private void AddHeldBlock(MyCubeBlockDefinition held, MatrixD reference, double blocksMultiplier, ref PanelContext context)
    {
        var model = scanner.Model;
        int copies = Math.Max(1, placements.Count);

        model.BaseMass += held.Mass * copies;
        if (!double.IsNaN(model.PhysicalMass))
            model.PhysicalMass += held.Mass * copies;

        context.HeldBlockName = held.DisplayNameText;
        context.HeldCount = copies;
        context.HeldMass = held.Mass * copies;

        if (held is MyCargoContainerDefinition cargo)
        {
            var (heavy, max) = ItemDensity.Unconstrained();
            double volume = cargo.InventorySize.X * cargo.InventorySize.Y * cargo.InventorySize.Z * blocksMultiplier;
            for (int i = 0; i < copies; i++)
                model.Cargo.Add(new CargoSlot { VolumeM3 = volume, HeavyDensity = heavy, MaxDensity = max });
        }

        if (!(held is MyThrustDefinition thrust))
            return;

        if (placements.Count == 0)
        {
            context.HeldPush = "?";
            return;
        }

        // Built fresh each time: its fuel index belongs to the current scan.
        var spec = scanner.SpecFor(thrust, out _);

        var toReference = MatrixD.Transpose(reference);
        int first = model.Thrusters.Count;
        for (int i = 0; i < placements.Count; i++)
            model.Thrusters.Add(spec);
        model.EnsureCapacity();

        Array.Clear(pushBins, 0, 6);
        for (int i = 0; i < placements.Count; i++)
        {
            var push = Vector3D.TransformNormal(placements[i].Backward, toReference);
            int k = first + i;
            model.Working[k] = true;
            model.PushDirections[k * 3] = push.X;
            model.PushDirections[k * 3 + 1] = push.Y;
            model.PushDirections[k * 3 + 2] = push.Z;
            if (i == 0)
                ThrustMath.AddDirectional(pushBins, push.X, push.Y, push.Z, 1.0);
        }

        int best = 0;
        for (int d = 1; d < 6; d++)
            if (pushBins[d] > pushBins[best])
                best = d;
        context.HeldPush = ((Dir)best).ToString();
    }

    /// <summary>
    /// With real cargo aboard, checks which rule the physics engine uses for cargo mass by
    /// comparing its mass with the block mass. Picks the nearest of: divided by the block
    /// inventory multiplier, divided by the character one, or counted in full.
    /// </summary>
    private void MeasureCargoFactor(IMyShipController controller, ShipModel model, double characterMultiplier, double blocksMultiplier)
    {
        if (!double.IsNaN(measuredCargoFactor) || double.IsNaN(model.PhysicalMass) || model.CargoMass < 2000)
            return;

        // Only when no ships are docked by connector, so both masses cover the same grids
        physicalGroup.Clear();
        MyAPIGateway.GridGroups.GetGroup(controller.CubeGrid, GridLinkTypeEnum.Physical, physicalGroup);
        if (physicalGroup.Count != scanner.Grids.Count)
            return;

        double pilot = controller is IMyCockpit ? 100.0 : 0.0;
        double counted = model.PhysicalMass - controller.CalculateShipMass().BaseMass - pilot;
        double measured = counted / model.CargoMass;
        if (measured <= 0)
            return;

        double best = double.NaN, bestDistance = double.MaxValue;
        Consider(1.0 / blocksMultiplier, measured, ref best, ref bestDistance);
        Consider(1.0 / characterMultiplier, measured, ref best, ref bestDistance);
        Consider(1.0, measured, ref best, ref bestDistance);

        if (bestDistance < Math.Log(1.5))
        {
            measuredCargoFactor = best;
            Log.Info("Cargo mass counts at x" + best.ToString("0.###") + " in this world");
        }
    }

    private static void Consider(double candidate, double measured, ref double best, ref double bestDistance)
    {
        double distance = Math.Abs(Math.Log(measured / candidate));
        if (distance < bestDistance)
        {
            bestDistance = distance;
            best = candidate;
        }
    }

    private void Hide()
    {
        if (mode == Mode.None && ops.Count == 0)
            return;
        mode = Mode.None;
        ops.Clear();
        lastHeldCount = -1;
        lastPushes.Clear();
        scanner.Detach();
    }

    private void Fail(string what, Exception e)
    {
        failed = true;
        ops.Clear();
        Log.Error(what + ", Thrust Preview is off until the world is reloaded", e);
        try
        {
            scanner.Detach();
            MyAPIGateway.Utilities?.ShowNotification("Thrust Preview stopped after an error, see the game log", 5000, "Red");
        }
        catch
        {
            // Already failing, nothing more to do
        }
    }
}
