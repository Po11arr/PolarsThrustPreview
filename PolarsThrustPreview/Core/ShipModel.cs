using System;
using System.Collections.Generic;

namespace PolarsThrustPreview.Core;

/// <summary>One fuel the ship's thrusters burn (Hydrogen in vanilla), with what the tanks hold.</summary>
public sealed class FuelPool
{
    public string Name;
    public double StoredLitres;
    public double CapacityLitres;
}

/// <summary>One inventory: its volume and the densest items it accepts, kg/m3.</summary>
public struct CargoSlot
{
    public double VolumeM3;
    public double HeavyDensity;
    public double MaxDensity;
}

/// <summary>Thrust in the six directions plus what the up thrusters burn at full throttle.</summary>
public sealed class ThrustResult
{
    public readonly double[] Thrust = new double[6];
    public double UpElectricMW;
    public double[] UpFuelLitresPerSecond = Array.Empty<double>();

    public double this[Dir d] => Thrust[(int)d];
}

/// <summary>
/// Plain numbers describing a ship. The game-facing scanner fills it; everything here is
/// pure so the maths can be tested outside the game.
/// </summary>
public sealed class ShipModel
{
    public readonly List<ThrusterSpec> Thrusters = new();

    /// <summary>Unit push direction per thruster in the reference frame, x/y/z interleaved.</summary>
    public double[] PushDirections = Array.Empty<double>();

    /// <summary>Thrusters that are not built far enough to work. They are skipped.</summary>
    public bool[] Working = Array.Empty<bool>();

    public readonly List<FuelPool> Fuels = new();
    public readonly List<CargoSlot> Cargo = new();

    public double BatteryStoredMWh;
    public double BatteryCapacityMWh;

    /// <summary>Output of reactors, engines, solar and wind, MW. Batteries excluded.</summary>
    public double GeneratorMW;

    /// <summary>Blocks only, kg.</summary>
    public double BaseMass;

    /// <summary>Mass the physics engine uses now, kg, or NaN when not available (static grid).</summary>
    public double PhysicalMass = double.NaN;

    /// <summary>Real mass of the items carried now, kg.</summary>
    public double CargoMass;

    /// <summary>Share of cargo mass the physics engine counts in this world (1 / inventory multiplier in vanilla).</summary>
    public double WorldCargoMassFactor = 1.0;

    /// <summary>Share of cargo mass counted for the heavy and max loads, at the inventory size chosen in the config.</summary>
    public double CargoMassFactor = 1.0;

    /// <summary>Thrust multiplier from the config, for what-if checks.</summary>
    public double ThrustScale = 1.0;

    /// <summary>Scales inventory volume from the world's multiplier to the chosen one.</summary>
    public double VolumeScale = 1.0;

    /// <summary>Grows the per-thruster arrays to the thruster count, keeping what is in them.</summary>
    public void EnsureCapacity()
    {
        int n = Thrusters.Count;
        if (PushDirections.Length < n * 3)
            Array.Resize(ref PushDirections, Math.Max(n * 3, PushDirections.Length * 2));
        if (Working.Length < n)
            Array.Resize(ref Working, Math.Max(n, Working.Length * 2));
    }

    public double TotalCargoVolume
    {
        get
        {
            double v = 0;
            foreach (var slot in Cargo)
                v += slot.VolumeM3;
            return v * VolumeScale;
        }
    }

    /// <summary>Mass now, as the physics engine sees it (world rules, whatever inventory size is chosen).</summary>
    public double MassNow => double.IsNaN(PhysicalMass) ? BaseMass + CargoMass * WorldCargoMassFactor : PhysicalMass;

    /// <summary>Mass with every inventory full of the given density. Contents today are replaced.</summary>
    public double MassFull(bool heaviest)
    {
        double cargo = 0;
        foreach (var slot in Cargo)
            cargo += slot.VolumeM3 * (heaviest ? slot.MaxDensity : slot.HeavyDensity);

        // Base taken from the physics value when present so "now" and the presets agree.
        // Physics counted today's cargo at the world factor, so it comes off at that factor.
        double empty = double.IsNaN(PhysicalMass) ? BaseMass : PhysicalMass - CargoMass * WorldCargoMassFactor;
        if (empty < BaseMass * 0.5)
            empty = BaseMass;

        return empty + cargo * VolumeScale * CargoMassFactor;
    }

    /// <summary>Thrust per direction in the given surroundings, with full-throttle burn of the up thrusters.</summary>
    public void Evaluate(Surroundings env, ThrustResult result)
    {
        Array.Clear(result.Thrust, 0, 6);
        result.UpElectricMW = 0;
        if (result.UpFuelLitresPerSecond.Length != Fuels.Count)
            result.UpFuelLitresPerSecond = new double[Fuels.Count];
        else
            Array.Clear(result.UpFuelLitresPerSecond, 0, Fuels.Count);

        for (int i = 0; i < Thrusters.Count; i++)
        {
            if (!Working[i])
                continue;

            var t = Thrusters[i];
            double eff = ThrustMath.Effectiveness(t, env.AirDensity, env.HasAtmosphere);
            if (eff <= 0)
                continue;

            double x = PushDirections[i * 3], y = PushDirections[i * 3 + 1], z = PushDirections[i * 3 + 2];
            ThrustMath.AddDirectional(result.Thrust, x, y, z, t.Force * ThrustScale * eff);

            // Burn counted for thrusters whose main push is up (the game throttles them as
            // one group). Power and fuel scale with effectiveness, as in MyEntityThrustComponent.
            if (y <= 0 || y < Math.Abs(x) || y < Math.Abs(z))
                continue;
            double powerMW = t.MaxPowerMW * eff;
            if (t.Fuel < 0)
                result.UpElectricMW += powerMW;
            else if (t.Fuel < Fuels.Count)
                result.UpFuelLitresPerSecond[t.Fuel] += powerMW * t.LitresPerMWs;
        }
    }
}
