using System;
using System.Reflection;
using PolarsThrustPreview.Settings;
using PolarsThrustPreview.Settings.Layouts;
using Sandbox.Graphics.GUI;
using VRage.Plugins;

// Define assembly version when compiled by Pulsar
#if !LOCAL_BUILD
[assembly: AssemblyVersion("1.2.5.0")]
[assembly: AssemblyFileVersion("1.2.5.0")]
#endif

namespace PolarsThrustPreview;

/// <summary>
/// Entry point. All the work happens in <see cref="Game.ThrustPreviewSession"/>, a session
/// component the game creates and unloads with each world, so nothing here outlives a world.
/// No Harmony patches.
/// </summary>
// ReSharper disable once UnusedType.Global
public class Plugin : IPlugin
{
    public const string Name = "PolarsThrustPreview";
    private SettingsGenerator settingsGenerator;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void Init(object gameInstance)
    {
        try
        {
            var config = Config.Current; // loads the config, or writes it with defaults on first run
            Log.Info("Loaded " + Assembly.GetExecutingAssembly().GetName().Version + (config.Enabled ? "" : ", turned off in settings"));
        }
        catch (Exception e)
        {
            Log.Error("Failed to load", e);
        }
    }

    public void Dispose()
    {
    }

    public void Update()
    {
    }

    // ReSharper disable once UnusedMember.Global
    public void OpenConfigDialog()
    {
        try
        {
            settingsGenerator ??= new SettingsGenerator();
            settingsGenerator.SetLayout<Simple>();
            MyGuiSandbox.AddScreen(settingsGenerator.Dialog);
        }
        catch (Exception e)
        {
            Log.Error("Could not open the settings", e);
        }
    }
}
