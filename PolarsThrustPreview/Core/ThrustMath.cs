using System;

namespace PolarsThrustPreview.Core;

/// <summary>Six ship directions, in the reference block's frame (cockpit, or grid when none).</summary>
public enum Dir
{
    Forward = 0,
    Backward = 1,
    Up = 2,
    Down = 3,
    Left = 4,
    Right = 5,
}

/// <summary>Per-thruster numbers, read once from the definition when the ship is scanned.</summary>
public sealed class ThrusterSpec
{
    /// <summary>Force at 100% effectiveness, newtons, block thrust multiplier included.</summary>
    public double Force;

    /// <summary>Power at full throttle and 100% effectiveness, MW, block power multiplier included.</summary>
    public double MaxPowerMW;

    public float MinInfluence;
    public float MaxInfluence;
    public float EffectivenessAtMin;
    public float EffectivenessAtMax;
    public bool NeedsAtmosphere;

    /// <summary>Index into the ship's fuel list, or -1 for electric thrusters.</summary>
    public int Fuel = -1;

    /// <summary>Litres of fuel per MW-second: 1 / (converter efficiency * fuel energy density).</summary>
    public double LitresPerMWs;
}

/// <summary>Where the ship is being judged: air density 0..1, atmosphere present, gravity in m/s2.</summary>
public struct Surroundings
{
    public float AirDensity;
    public bool HasAtmosphere;
    public double Gravity;

    public Surroundings(float airDensity, bool hasAtmosphere, double gravity)
    {
        AirDensity = airDensity;
        HasAtmosphere = hasAtmosphere;
        Gravity = gravity;
    }
}

/// <summary>Pure thrust maths, no game types, so it can be checked outside the game.</summary>
public static class ThrustMath
{
    public const double G = 9.81;

    /// <summary>
    /// Same rule as the game's thruster component (MyThrusterBlockThrustComponent.CalculateForceMultiplier):
    /// atmosphere-only thrusters get the minimum value without an atmosphere, otherwise the
    /// effectiveness is lerped across the influence range by air density.
    /// </summary>
    public static double Effectiveness(ThrusterSpec t, float airDensity, bool hasAtmosphere)
    {
        if (t.NeedsAtmosphere && !hasAtmosphere)
            return t.EffectivenessAtMin;

        if (t.MaxInfluence != t.MinInfluence)
        {
            double k = (airDensity - t.MinInfluence) / (t.MaxInfluence - t.MinInfluence);
            if (k < 0) k = 0;
            else if (k > 1) k = 1;
            return t.EffectivenessAtMin + (t.EffectivenessAtMax - t.EffectivenessAtMin) * k;
        }

        return 1.0;
    }

    /// <summary>Push components smaller than this are rounding noise from the world matrices, not thrust.</summary>
    public const double DirectionNoise = 1e-4;

    /// <summary>
    /// Adds a force along a unit push direction (reference frame, VRage axes: +X right, +Y up,
    /// -Z forward) to the six direction bins. Angled thrusters count by their component.
    /// Components under <see cref="DirectionNoise"/> are dropped so an empty direction stays exactly 0.
    /// </summary>
    public static void AddDirectional(double[] bins, double x, double y, double z, double amount)
    {
        if (Math.Abs(x) < DirectionNoise) x = 0;
        if (Math.Abs(y) < DirectionNoise) y = 0;
        if (Math.Abs(z) < DirectionNoise) z = 0;

        if (x > 0) bins[(int)Dir.Right] += amount * x;
        else if (x < 0) bins[(int)Dir.Left] -= amount * x;

        if (y > 0) bins[(int)Dir.Up] += amount * y;
        else if (y < 0) bins[(int)Dir.Down] -= amount * y;

        if (z < 0) bins[(int)Dir.Forward] -= amount * z;
        else if (z > 0) bins[(int)Dir.Backward] += amount * z;
    }

    /// <summary>Thrust-to-weight ratio. Zero mass or gravity gives 0.</summary>
    public static double Twr(double thrust, double mass, double gravity)
    {
        if (mass <= 0 || gravity <= 0)
            return 0;
        return thrust / (mass * gravity);
    }

    /// <summary>Throttle needed to hover, 0..1, or above 1 when the ship cannot hover.</summary>
    public static double HoverThrottle(double upThrust, double mass, double gravity)
    {
        if (upThrust <= 0)
            return double.PositiveInfinity;
        return mass * gravity / upThrust;
    }

    /// <summary>Seconds until a store runs dry at the given drain. Infinity when nothing drains.</summary>
    public static double Endurance(double stored, double drainPerSecond)
    {
        if (drainPerSecond <= 1e-12)
            return double.PositiveInfinity;
        return Math.Max(0, stored) / drainPerSecond;
    }

    /// <summary>
    /// Physics share of cargo mass after a change of inventory size. The game divides cargo mass
    /// by the inventory multiplier (MyGridShape.UpdateMassFromInventories). If the world factor
    /// does not match that rule (a mod changed it), it is kept as it is.
    /// </summary>
    public static double CargoMassFactor(double worldFactor, double worldMultiplier, double chosenMultiplier)
    {
        if (worldMultiplier <= 0 || chosenMultiplier <= 0)
            return worldFactor;

        double expected = 1.0 / worldMultiplier;
        bool followsRule = Math.Abs(worldFactor - expected) <= expected * 0.05;
        return followsRule ? 1.0 / chosenMultiplier : worldFactor;
    }
}
