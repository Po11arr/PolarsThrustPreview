using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using PolarsThrustPreview.Settings;
using PolarsThrustPreview.Settings.Elements;
using PolarsThrustPreview.Settings.Tools;
using VRage.Input;

namespace PolarsThrustPreview;

public class Config : INotifyPropertyChanged
{
    #region Options

    private bool enabled = true;
    // "/" (OemQuestion) has no vanilla control binding and the game never reads it; it is the
    // / ? key next to right Shift on both US and UK layouts.
    // Old defaults clashed: Ctrl+J also toggled the helmet, "]" is next paint colour, skin and
    // camera, OemQuotes is ' on US layouts but # on UK ones, where ' is toolbar slot 0, F2 is
    // used by Text HUD mod menus and F11 opens a game menu.
    private Binding toggleKey = new(MyKeys.OemQuestion);
    private bool showWhileBuilding = true;
    private float inventoryMultiplier;
    private float thrustMultiplier = 1f;
    private float textScale = 0.8f;
    private float positionX = 0.01f;
    private float positionY = 0.28f;

    #endregion

    #region User interface

    public readonly string Title = "Polar's Thrust Preview";

    [Checkbox(description: "Turn the whole plugin on or off")]
    public bool Enabled
    {
        get => enabled;
        set => SetField(ref enabled, value);
    }

    [Keybind(label: "Panel key", description: "Default ]. Seated: cycles full, collapsed, off. Holding a block: cycles full, collapsed, hidden. Right click to unbind")]
    public Binding ToggleKey
    {
        get => toggleKey;
        set => SetField(ref toggleKey, value);
    }

    [Checkbox(label: "Show while building", description: "Show the panel, with the held block added, while placing blocks on a grid")]
    public bool ShowWhileBuilding
    {
        get => showWhileBuilding;
        set => SetField(ref showWhileBuilding, value);
    }

    [Slider(0f, 100f, 0.5f, SliderAttribute.SliderType.Float, label: "Inventory size (0 = world)",
        description: "Inventory multiplier to plan for. 0 uses the world setting")]
    public float InventoryMultiplier
    {
        get => inventoryMultiplier;
        set => SetField(ref inventoryMultiplier, value);
    }

    [Slider(0.1f, 10f, 0.1f, SliderAttribute.SliderType.Float, label: "Thrust multiplier",
        description: "What-if multiplier on all thrust. 1 = as built")]
    public float ThrustMultiplier
    {
        get => thrustMultiplier;
        set => SetField(ref thrustMultiplier, value);
    }

    [Slider(0.5f, 1.5f, 0.05f, SliderAttribute.SliderType.Float, label: "Text size")]
    public float TextScale
    {
        get => textScale;
        set => SetField(ref textScale, value);
    }

    [Slider(0f, 1f, 0.01f, SliderAttribute.SliderType.Float, label: "Position X", description: "0 = left edge, 1 = right edge")]
    public float PositionX
    {
        get => positionX;
        set => SetField(ref positionX, value);
    }

    [Slider(0f, 1f, 0.01f, SliderAttribute.SliderType.Float, label: "Position Y", description: "0 = top edge, 1 = bottom edge")]
    public float PositionY
    {
        get => positionY;
        set => SetField(ref positionY, value);
    }

    #endregion

    /// <summary>Config layout version, for upgrading old files. Not shown in the settings.</summary>
    public int SchemaVersion { get; set; }

    private const int CurrentSchema = 6;

    /// <summary>Clamps values from a hand-edited file and upgrades old ones. True when anything changed.</summary>
    public bool Validate()
    {
        bool changed = false;
        if (SchemaVersion < CurrentSchema)
        {
            // Earlier defaults, each of which clashed with a vanilla control
            bool plain = !toggleKey.Ctrl && !toggleKey.Alt && !toggleKey.Shift;
            if (SchemaVersion < 2 && toggleKey.Key == MyKeys.J && toggleKey.Ctrl && !toggleKey.Alt && !toggleKey.Shift)
                toggleKey = new Binding(MyKeys.OemQuestion);
            else if (SchemaVersion < 3 && plain && toggleKey.Key == MyKeys.OemCloseBrackets)
                toggleKey = new Binding(MyKeys.OemQuestion);
            else if (SchemaVersion < 4 && plain && toggleKey.Key == MyKeys.OemQuotes)
                toggleKey = new Binding(MyKeys.OemQuestion);
            else if (SchemaVersion < 5 && plain && toggleKey.Key == MyKeys.F2)
                toggleKey = new Binding(MyKeys.OemQuestion);
            else if (SchemaVersion < 6 && plain && toggleKey.Key == MyKeys.F11)
                toggleKey = new Binding(MyKeys.OemQuestion);
            SchemaVersion = CurrentSchema;
            changed = true;
        }

        changed |= Clamp(ref inventoryMultiplier, 0f, 100f, 0f);
        changed |= Clamp(ref thrustMultiplier, 0.1f, 10f, 1f);
        changed |= Clamp(ref textScale, 0.5f, 1.5f, 0.8f);
        changed |= Clamp(ref positionX, 0f, 1f, 0.01f);
        changed |= Clamp(ref positionY, 0f, 1f, 0.28f);
        return changed;
    }

    /// <summary>Clamps into range (NaN and infinity become the fallback). True if the value changed.</summary>
    private static bool Clamp(ref float value, float min, float max, float fallback)
    {
        var clamped = float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Max(min, Math.Min(max, value));
        if (clamped == value)
            return false;
        value = clamped;
        return true;
    }

    #region Property change notification boilerplate

    public static readonly Config Current = ConfigStorage.Load();

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        ConfigStorage.Dirty = true;
        OnPropertyChanged(propertyName);
        return true;
    }

    #endregion
}
