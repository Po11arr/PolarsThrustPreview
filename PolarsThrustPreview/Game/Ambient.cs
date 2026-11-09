using PolarsThrustPreview.Core;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRageMath;

namespace PolarsThrustPreview.Game;

/// <summary>Air density and natural gravity at a point.</summary>
internal static class Ambient
{
    public static Surroundings At(Vector3D position)
    {
        var gravity = MyAPIGateway.Physics.CalculateNaturalGravityAt(position, out _);
        var planet = MyGamePruningStructure.GetClosestPlanet(position);
        float air = planet?.GetAirDensity(position) ?? 0f;
        bool hasAtmosphere = planet != null && planet.HasAtmosphere;
        return new Surroundings(air, hasAtmosphere, gravity.Length());
    }
}
