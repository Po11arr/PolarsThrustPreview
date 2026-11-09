using System;
using System.IO;
using System.Xml.Serialization;
using VRage.FileSystem;

namespace PolarsThrustPreview.Settings;

/// <summary>
/// Config in the game's Storage folder. Missing file: written with defaults. Unreadable file:
/// defaults are used and the file is left alone until a setting is changed in the settings dialog.
/// Unknown or missing fields from other versions fall back to their defaults.
/// </summary>
public static class ConfigStorage
{
    /// <summary>A setting changed since the last load or save.</summary>
    public static bool Dirty;

    private static readonly string ConfigFileName = string.Concat(Plugin.Name, ".cfg");
    private static string ConfigFilePath => Path.Combine(MyFileSystem.UserDataPath, "Storage", ConfigFileName);

    public static void Save(Config config)
    {
        try
        {
            var path = ConfigFilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var text = File.CreateText(path))
                new XmlSerializer(typeof(Config)).Serialize(text, config);
            Dirty = false;
        }
        catch (Exception e)
        {
            Log.Error("Could not save " + ConfigFileName, e);
        }
    }

    /// <summary>Defaults stamped with the current schema, so a later save is not migrated again.</summary>
    private static Config Defaults()
    {
        var config = new Config();
        config.Validate();
        return config;
    }

    public static Config Load()
    {
        string path;
        try
        {
            path = ConfigFilePath;
            if (!File.Exists(path))
            {
                var defaults = Defaults();
                Save(defaults);
                return defaults;
            }
        }
        catch (Exception e)
        {
            Log.Error("Could not find the config folder, using defaults", e);
            return Defaults();
        }

        Config config;
        try
        {
            using (var streamReader = File.OpenText(path))
            {
                config = (Config)new XmlSerializer(typeof(Config)).Deserialize(streamReader) ?? new Config();
            }
        }
        catch (Exception e)
        {
            Log.Error("Could not read " + path + ", using defaults", e);
            Dirty = false;
            return Defaults();
        }

        // Upgraded or clamped values are written back, after the file is closed
        if (config.Validate())
            Save(config);
        Dirty = false;
        return config;
    }
}
