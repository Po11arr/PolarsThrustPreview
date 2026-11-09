using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using VRage.Game.ModAPI;
using VRageMath;

namespace PolarsThrustPreview.Game;

/// <summary>
/// Reads the block the player is about to place. The held block definition is public; the
/// target grid and the gizmo (orientation, symmetry copies) are not, so they are read through
/// getters compiled once. If any member is missing after a game update the orientation part
/// switches off and the preview falls back to mass only.
/// </summary>
internal static class BuilderAccess
{
    private static bool resolved;
    private static Func<object, object> getCurrentGrid;
    private static Func<object, object> getGizmo;
    private static Func<object, object> getSpaces;
    private static Func<object, object> getSpaceEnabled;
    private static Func<object, object> getSpaceWorldMatrix;

    public static bool OrientationAvailable { get; private set; }

    public static void Reset()
    {
        resolved = false;
        getCurrentGrid = getGizmo = getSpaces = getSpaceEnabled = getSpaceWorldMatrix = null;
        OrientationAvailable = false;
    }

    /// <summary>
    /// Held block and target grid, plus the world matrix of every copy that would be placed
    /// (one, or more with symmetry). Returns false when nothing is being placed on a grid.
    /// </summary>
    public static bool TryGetHeld(out MyCubeBlockDefinition definition, out IMyCubeGrid grid, List<MatrixD> placements)
    {
        definition = null;
        grid = null;
        placements.Clear();

        var builder = MyCubeBuilder.Static;
        if (builder == null || !builder.IsActivated)
            return false;

        definition = builder.CubeBuilderState?.CurrentBlockDefinition;
        if (definition == null)
            return false;

        if (!resolved)
            Resolve(builder.GetType());

        // CurrentGrid is null when aiming at empty space (a new grid): nothing to add to.
        // The closest grid is only a stand-in when the member itself is missing.
        grid = getCurrentGrid != null
            ? getCurrentGrid(builder) as IMyCubeGrid
            : builder.FindClosestGrid();
        if (grid == null)
            return false;

        if (OrientationAvailable)
            ReadPlacements(builder, placements);

        return true;
    }

    private static void ReadPlacements(object builder, List<MatrixD> placements)
    {
        try
        {
            var gizmo = getGizmo(builder);
            if (gizmo == null || !(getSpaces(gizmo) is Array spaces))
                return;

            foreach (var space in spaces)
            {
                if (space == null || !(getSpaceEnabled(space) is bool enabled) || !enabled)
                    continue;
                if (getSpaceWorldMatrix(space) is MatrixD world)
                    placements.Add(world);
            }
        }
        catch (Exception e)
        {
            OrientationAvailable = false;
            placements.Clear();
            Log.Error("Could not read the build gizmo, held block direction is off", e);
        }
    }

    private static void Resolve(Type builderType)
    {
        resolved = true;
        try
        {
            getCurrentGrid = CompileGetter(builderType, "CurrentGrid") ?? CompileGetter(builderType, "m_currentGrid");
            getGizmo = CompileGetter(builderType, "m_gizmo");

            var gizmoType = FindMemberType(builderType, "m_gizmo");
            getSpaces = gizmoType == null ? null : CompileGetter(gizmoType, "Spaces");

            var spaceType = gizmoType?.GetProperty("Spaces", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.PropertyType.GetElementType();
            getSpaceEnabled = spaceType == null ? null : CompileGetter(spaceType, "Enabled");
            getSpaceWorldMatrix = spaceType == null ? null : CompileGetter(spaceType, "m_worldMatrixAdd");

            OrientationAvailable = getGizmo != null && getSpaces != null && getSpaceEnabled != null && getSpaceWorldMatrix != null;
            if (!OrientationAvailable)
                Log.Info("Build gizmo members not found, the held block counts for mass only");
            if (getCurrentGrid == null)
                Log.Info("Build target grid member not found, using the closest grid");
        }
        catch (Exception e)
        {
            OrientationAvailable = false;
            Log.Error("Could not inspect the cube builder, the held block counts for mass only", e);
        }
    }

    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static Type FindMemberType(Type type, string name)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            var field = t.GetField(name, Members);
            if (field != null)
                return field.FieldType;
            var property = t.GetProperty(name, Members);
            if (property != null)
                return property.PropertyType;
        }
        return null;
    }

    /// <summary>Compiles a boxed getter for a field or property, searching base types. Null if not found.</summary>
    private static Func<object, object> CompileGetter(Type type, string name)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            MemberInfo member = t.GetField(name, Members);
            if (member == null)
            {
                var property = t.GetProperty(name, Members);
                if (property?.GetGetMethod(true) != null)
                    member = property;
            }
            if (member == null)
                continue;

            var instance = Expression.Parameter(typeof(object), "instance");
            var typed = Expression.Convert(instance, t);
            Expression access = member is FieldInfo f ? Expression.Field(typed, f) : Expression.Property(typed, (PropertyInfo)member);
            var body = Expression.Convert(access, typeof(object));
            return Expression.Lambda<Func<object, object>>(body, instance).Compile();
        }
        return null;
    }
}
