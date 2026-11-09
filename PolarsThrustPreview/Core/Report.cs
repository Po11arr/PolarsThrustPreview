using System.Collections.Generic;

namespace PolarsThrustPreview.Core;

public enum Load
{
    Now = 0,
    Heavy = 1,
    Max = 2,
}

public sealed class FuelEndurance
{
    public string Name;
    public double SecondsNow;
    public double SecondsFull;
}

/// <summary>Everything the panel shows, computed from a <see cref="ShipModel"/>. No game types.</summary>
public sealed class Report
{
    public readonly double[] Mass = new double[3];

    /// <summary>TWR per load and direction, here, against <see cref="ReferenceG"/>.</summary>
    public readonly double[,] Twr = new double[3, 6];

    /// <summary>Gravity the TWR table is measured against, in g.</summary>
    public double ReferenceG;

    /// <summary>True when the ship is in natural gravity.</summary>
    public bool InGravity;

    /// <summary>Highest gravity, in g, the ship can hover in at sea-level air, per load.</summary>
    public readonly double[] LiftG = new double[3];

    /// <summary>Directions with no thrust at all in vacuum.</summary>
    public readonly bool[] NoSpaceThrust = new bool[6];

    /// <summary>Hover burn with the heavy load at 1 g sea level.</summary>
    public double HoverThrottle;
    public readonly List<FuelEndurance> Fuels = new();
    public bool HasElectricLift;
    public double ElectricSecondsNow;
    public double ElectricSecondsFull;
    public bool ElectricSustained;

    public double CargoVolumeM3;

    private readonly ThrustResult scratch = new();

    public static readonly Surroundings SeaLevel = new(1f, true, ThrustMath.G);
    public static readonly Surroundings Vacuum = new(0f, false, 0);

    public void Compute(ShipModel model, Surroundings here)
    {
        Mass[(int)Load.Now] = model.MassNow;
        Mass[(int)Load.Heavy] = model.MassFull(heaviest: false);
        Mass[(int)Load.Max] = model.MassFull(heaviest: true);
        CargoVolumeM3 = model.TotalCargoVolume;

        InGravity = here.Gravity > 0.05 * ThrustMath.G;
        ReferenceG = InGravity ? here.Gravity / ThrustMath.G : 1.0;
        double refGravity = ReferenceG * ThrustMath.G;

        // Table, here
        model.Evaluate(here, scratch);
        for (int load = 0; load < 3; load++)
        for (int d = 0; d < 6; d++)
            Twr[load, d] = ThrustMath.Twr(scratch.Thrust[d], Mass[load], refGravity);

        // Space: any thrust at all in each direction
        model.Evaluate(Vacuum, scratch);
        for (int d = 0; d < 6; d++)
            NoSpaceThrust[d] = scratch.Thrust[d] <= 0;

        // Lift limit at sea-level air: up thrust / weight at 1 g is the gravity it can hold, in g
        model.Evaluate(SeaLevel, scratch);
        for (int load = 0; load < 3; load++)
            LiftG[load] = ThrustMath.Twr(scratch[Dir.Up], Mass[load], ThrustMath.G);

        // Hover endurance with the heavy load at 1 g sea level (evaluates last, scratch holds it)
        model.Evaluate(SeaLevel, scratch);
        HoverThrottle = ThrustMath.HoverThrottle(scratch[Dir.Up], Mass[(int)Load.Heavy], SeaLevel.Gravity);

        Fuels.Clear();
        HasElectricLift = scratch.UpElectricMW > 0;
        ElectricSustained = false;
        ElectricSecondsNow = ElectricSecondsFull = 0;

        if (HoverThrottle > 1.0)
            return;

        for (int f = 0; f < model.Fuels.Count; f++)
        {
            double drain = scratch.UpFuelLitresPerSecond[f] * HoverThrottle;
            if (drain <= 0)
                continue;
            var pool = model.Fuels[f];
            Fuels.Add(new FuelEndurance
            {
                Name = pool.Name,
                SecondsNow = ThrustMath.Endurance(pool.StoredLitres, drain),
                SecondsFull = ThrustMath.Endurance(pool.CapacityLitres, drain),
            });
        }

        if (HasElectricLift)
        {
            double netMW = scratch.UpElectricMW * HoverThrottle - model.GeneratorMW;
            if (netMW <= 0)
            {
                ElectricSustained = true;
            }
            else
            {
                // MWh / MW = hours
                ElectricSecondsNow = ThrustMath.Endurance(model.BatteryStoredMWh * 3600.0, netMW);
                ElectricSecondsFull = ThrustMath.Endurance(model.BatteryCapacityMWh * 3600.0, netMW);
            }
        }
    }
}
