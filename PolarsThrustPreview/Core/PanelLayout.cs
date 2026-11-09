using System;
using System.Collections.Generic;
using System.Globalization;

namespace PolarsThrustPreview.Core;

public struct Rgba
{
    public byte R, G, B, A;

    public Rgba(byte r, byte g, byte b, byte a = 255)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }
}

public enum Align
{
    Left,
    Center,
    Right,
}

/// <summary>
/// One thing to draw, in panel pixels (designed for a 1080p screen, scaled when drawn).
/// Rectangles use X, Y, W, H. Text is vertically centred on Y and anchored at X by <see cref="Align"/>.
/// </summary>
public struct DrawOp
{
    public bool IsText;
    public float X, Y, W, H;
    public string Text;

    /// <summary>Text line height in panel pixels.</summary>
    public float Size;

    public Rgba Color;
    public Align Align;
}

/// <summary>What the panel needs beyond the report: names and the build-mode line.</summary>
public struct PanelContext
{
    public string ShipName;
    public bool Building;
    public string HeldBlockName;
    public int HeldCount;
    public double HeldMass;
    public string HeldPush;
    public double ChosenInventoryMultiplier;
    public bool InventoryOverridden;
    public double ThrustMultiplier;
    public bool StaticGrid;
}

/// <summary>Full panel, or the small one with weight, Up thrust to weight and lift.</summary>
public enum PanelView
{
    Full,
    Collapsed,
}

/// <summary>Lays the panel out as rectangles and text. Pure, no drawing.</summary>
public static class PanelLayout
{
    public const float Width = 470f;
    private const float Pad = 14f;
    private const float Right = Width - Pad;

    // Lift scale
    private const float LiftMaxG = 1.5f;
    private const float LiftBarX = 76f;
    private const float LiftBarW = 240f;

    // Thrust to weight table
    private const float NameW = 86f;
    private const float SpaceW = 84f;
    private const float TwrBarScale = 3f;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly string[] DirectionNames = { "Fwd", "Back", "Up", "Down", "Left", "Right" };
    private static readonly string[] LoadNames = { "current", "heavy", "max" };

    public static readonly Rgba Panel = new(8, 20, 34, 209);
    public static readonly Rgba Edge = new(110, 170, 220, 115);
    public static readonly Rgba HeaderFill = new(79, 163, 224, 31);
    public static readonly Rgba Accent = new(79, 163, 224);
    public static readonly Rgba Divider = new(110, 170, 220, 46);
    public static readonly Rgba Title = new(180, 220, 255);
    public static readonly Rgba Text = new(230, 238, 246);
    public static readonly Rgba Soft = new(200, 214, 227);
    public static readonly Rgba Dim = new(143, 166, 188);
    public static readonly Rgba Caption = new(127, 150, 172);
    public static readonly Rgba BoxFill = new(110, 170, 220, 20);
    public static readonly Rgba Track = new(255, 255, 255, 18);
    public static readonly Rgba Good = new(111, 210, 122);
    public static readonly Rgba Warn = new(242, 193, 78);
    public static readonly Rgba Bad = new(240, 99, 90);
    public static readonly Rgba NoLift = new(201, 72, 63);
    public static readonly Rgba Tick = new(8, 20, 34, 230);
    public static readonly Rgba PlaceFill = new(242, 193, 78, 31);
    public static readonly Rgba PlaceEdge = new(242, 193, 78, 90);
    public static readonly Rgba PlaceText = new(242, 213, 142);

    // Direction rows: one per direction, or a pair when both match
    private struct DirRow
    {
        public string Name;
        public int Dir;
        public bool IsLift;
    }

    private static readonly List<DirRow> rows = new(6);

    /// <summary>Builds the draw list. Returns the panel height in panel pixels.</summary>
    public static float Build(Report r, PanelContext c, PanelView view, List<DrawOp> ops)
    {
        ops.Clear();
        ops.Add(default); // background, sized at the end

        float y = Header(r, c, ops);

        if (view == PanelView.Collapsed)
        {
            y += 8;
            double upNow = r.Twr[(int)Load.Now, (int)Dir.Up];
            Label(ops, Pad, y + 11, "Current weight", 12.5f, Dim);
            Label(ops, 130, y + 11, Mass(r.Mass[(int)Load.Now]), 15, Text);
            Label(ops, Right - 46, y + 11, "Up thrust to weight", 12.5f, Dim, Align.Right);
            Label(ops, Right, y + 11, Twr(upNow), 15, r.InGravity ? LiftColor(upNow) : Text, Align.Right);
            y += 30;
            Rect(ops, 0, y, Width, 1, Divider);
            y = Lift(r, ops, y + 8, 2);
            return Finish(ops, y);
        }

        // Weight boxes
        y += 8;
        float boxW = (Width - 2 * Pad - 16) / 3f;
        string[] boxNames = { "Current weight", "Heavy (full of ore)", "Max (densest cargo)" };
        for (int i = 0; i < 3; i++)
        {
            float x = Pad + i * (boxW + 8);
            Rect(ops, x, y, boxW, 44, BoxFill);
            Label(ops, x + 8, y + 12, boxNames[i], 11, Dim);
            Label(ops, x + 8, y + 31, Mass(r.Mass[i]), 19, Text);
        }
        y += 44;

        string cargo = Volume(r.CargoVolumeM3) + " cargo at x" + Multiplier(c.ChosenInventoryMultiplier) + " inventory" + (c.InventoryOverridden ? " (set)" : "");
        Label(ops, Pad, y + 11, cargo, 11, Caption);
        if (Math.Abs(c.ThrustMultiplier - 1.0) > 1e-3)
            Label(ops, Right, y + 11, "thrust x" + Multiplier(c.ThrustMultiplier) + " (set)", 11, Warn, Align.Right);
        y += 22;
        Rect(ops, 0, y, Width, 1, Divider);

        // Thrust to weight table
        y += 8;
        string where = r.InGravity
            ? "THRUST TO WEIGHT RATIO AT " + Number(r.ReferenceG, 2) + " G, HERE"
            : "THRUST TO WEIGHT RATIO AT 1 G, IN SPACE";
        Label(ops, Pad, y + 8, where, 11, Caption);
        y += 18;

        float cellX = Pad + NameW;
        float cellW = (Right - SpaceW - cellX) / 3f;
        for (int load = 0; load < 3; load++)
            Label(ops, cellX + load * cellW, y + 8, LoadNames[load], 11, Caption);
        Label(ops, Right - SpaceW / 2, y + 8, "space capable", 11, Caption, Align.Center);
        y += 16;

        BuildRows(r);
        foreach (var row in rows)
        {
            float cy = y + 10;
            Label(ops, Pad, cy, row.Name, 12.5f, row.IsLift ? Text : Dim);
            for (int load = 0; load < 3; load++)
            {
                double twr = r.Twr[load, row.Dir];
                var tone = row.IsLift && r.InGravity ? LiftColor(twr) : MoveColor(twr);
                float x = cellX + load * cellW;
                float barW = cellW - 45;
                Rect(ops, x, cy - 3.5f, barW, 7, Track);
                Rect(ops, x, cy - 3.5f, barW * (float)Math.Min(1.0, Math.Max(0, twr) / TwrBarScale), 7, tone);
                Label(ops, x + cellW - 6, cy, Twr(twr), 12.5f, tone, Align.Right);
            }

            float sx = Right - SpaceW / 2;
            if (r.NoSpaceThrust[row.Dir])
                Label(ops, sx, cy, "no", 12f, Bad, Align.Center);
            else
                Label(ops, sx, cy, "yes", 12f, Good, Align.Center);
            y += 20;
        }
        y += 8;
        Rect(ops, 0, y, Width, 1, Divider);

        // Lift in gravity
        y = Lift(r, ops, y + 8, 3);
        Rect(ops, 0, y, Width, 1, Divider);

        // Hover time
        y += 8;
        Label(ops, Pad, y + 8, "HOVER TIME, HEAVY LOAD, AT 1 G", 11, Caption);
        y += 18;

        if (double.IsInfinity(r.HoverThrottle))
        {
            Label(ops, Pad, y + 11, "No thrust pushes up", 13, Bad);
            y += 22;
        }
        else if (r.HoverThrottle > 1.0)
        {
            Label(ops, Pad, y + 11, "Can't hover with a heavy load at 1 g", 13, Bad);
            y += 22;
        }
        else
        {
            foreach (var fuel in r.Fuels)
            {
                Label(ops, Pad, y + 11, Trim(fuel.Name, 12), 13, Soft);
                Label(ops, 120, y + 11, Time(fuel.SecondsNow), 13, EnduranceColor(fuel.SecondsNow));
                Label(ops, Right, y + 11, "full tanks: " + Time(fuel.SecondsFull), 12, Dim, Align.Right);
                y += 22;
            }

            if (r.HasElectricLift)
            {
                Label(ops, Pad, y + 11, "Batteries", 13, Soft);
                if (r.ElectricSustained)
                {
                    Label(ops, 120, y + 11, "covered by generators", 13, Good);
                }
                else
                {
                    Label(ops, 120, y + 11, Time(r.ElectricSecondsNow), 13, EnduranceColor(r.ElectricSecondsNow));
                    Label(ops, Right, y + 11, "full batteries: " + Time(r.ElectricSecondsFull), 12, Dim, Align.Right);
                }
                y += 22;
            }
        }
        y += 8;
        return Finish(ops, y);
    }

    private static float Header(Report r, PanelContext c, List<DrawOp> ops)
    {
        Rect(ops, 0, 0, Width, 34, HeaderFill);
        Rect(ops, 0, 34, Width, 2, Accent);
        Label(ops, Pad, 17, "THRUST PREVIEW", 16, Title);
        string name = Trim(c.ShipName, 32) + (c.StaticGrid ? " (static)" : "");
        Label(ops, Right, 17, name, 13, Dim, Align.Right);
        float y = 36;

        if (c.Building && !string.IsNullOrEmpty(c.HeldBlockName))
        {
            Rect(ops, 0, y, Width, 26, PlaceFill);
            Rect(ops, 0, y + 26, Width, 1, PlaceEdge);
            string count = c.HeldCount > 1 ? " x" + c.HeldCount : "";
            string push = string.IsNullOrEmpty(c.HeldPush) ? "" : "  |  pushes " + c.HeldPush;
            Label(ops, Pad, y + 13, "+ " + Trim(c.HeldBlockName, 34) + count + push + "  |  +" + Mass(c.HeldMass), 12.5f, PlaceText);
            y += 27;
        }
        return y;
    }

    /// <summary>Lift bars for the first <paramref name="loads"/> loads. Returns the y below the section.</summary>
    private static float Lift(Report r, List<DrawOp> ops, float y, int loads)
    {
        Label(ops, Pad, y + 8, "LIFTS IN GRAVITY", 11, Caption);
        y += 18;
        string[] scaleLabels = { "0g", "0.5g", "1.0g", "1.5g" };
        for (int i = 0; i < 4; i++)
            Label(ops, LiftX(i * 0.5), y + 7, scaleLabels[i], 10.5f, Caption, Align.Center);
        y += 14;

        bool markHere = r.InGravity && r.ReferenceG <= LiftMaxG;
        for (int load = 0; load < loads; load++)
        {
            float cy = y + 10;
            double limit = r.LiftG[load];
            double comfortable = limit / 1.25;
            Label(ops, Pad, cy, LoadNames[load], 12.5f, Soft);

            Rect(ops, LiftBarX, cy - 4.5f, LiftBarW, 9, NoLift);
            Rect(ops, LiftBarX, cy - 4.5f, LiftX(comfortable) - LiftBarX, 9, Good);
            Rect(ops, LiftX(comfortable), cy - 4.5f, LiftX(limit) - LiftX(comfortable), 9, Warn);
            Rect(ops, LiftX(0.5), cy - 4.5f, 1, 9, Tick);
            Rect(ops, LiftX(1.0), cy - 4.5f, 1, 9, Tick);
            if (markHere)
                Rect(ops, LiftX(r.ReferenceG) - 1, cy - 8.5f, 2, 17, Text);

            string limitText = limit < 0.005 ? "can't lift" : limit >= 99 ? "lifts up to 99+ g" : "lifts up to " + Number(limit, 2) + " g";
            Label(ops, Right, cy, limitText, 12f, LiftColor(limit), Align.Right);
            y += 20;
        }

        if (markHere)
        {
            Label(ops, LiftX(r.ReferenceG), y + 7, "you " + Number(r.ReferenceG, 2) + " g", 10.5f, Text, Align.Center);
            y += 14;
        }
        return y + 8;
    }

    /// <summary>Merges Fwd/Back, Up/Down and Left/Right into one row where both match for every load.</summary>
    private static void BuildRows(Report r)
    {
        rows.Clear();
        AddPair(r, (int)Dir.Forward, (int)Dir.Backward, "Fwd / Back", false);
        AddPair(r, (int)Dir.Up, (int)Dir.Down, "Up / Down", true);
        AddPair(r, (int)Dir.Left, (int)Dir.Right, "Left / Right", false);
    }

    private static void AddPair(Report r, int a, int b, string pairName, bool firstIsLift)
    {
        bool same = r.NoSpaceThrust[a] == r.NoSpaceThrust[b];
        for (int load = 0; load < 3 && same; load++)
        {
            double x = r.Twr[load, a], z = r.Twr[load, b];
            same = Math.Abs(x - z) <= 0.005 * Math.Max(1.0, Math.Max(x, z));
        }

        if (same)
        {
            rows.Add(new DirRow { Name = pairName, Dir = a, IsLift = firstIsLift });
            return;
        }
        rows.Add(new DirRow { Name = DirectionNames[a], Dir = a, IsLift = firstIsLift });
        rows.Add(new DirRow { Name = DirectionNames[b], Dir = b });
    }

    private static float Finish(List<DrawOp> ops, float y)
    {
        ops[0] = new DrawOp { X = 0, Y = 0, W = Width, H = y, Color = Panel };
        Rect(ops, 0, 0, Width, 1, Edge);
        Rect(ops, 0, y - 1, Width, 1, Edge);
        Rect(ops, 0, 0, 1, y, Edge);
        Rect(ops, Width - 1, 0, 1, y, Edge);
        return y;
    }

    private static float LiftX(double g) => LiftBarX + LiftBarW * (float)(Math.Min(Math.Max(g, 0), LiftMaxG) / LiftMaxG);

    private static void Rect(List<DrawOp> ops, float x, float y, float w, float h, Rgba color)
    {
        if (w > 0 && h > 0)
            ops.Add(new DrawOp { X = x, Y = y, W = w, H = h, Color = color });
    }

    private static void Label(List<DrawOp> ops, float x, float y, string text, float size, Rgba color, Align align = Align.Left)
    {
        if (!string.IsNullOrEmpty(text))
            ops.Add(new DrawOp { IsText = true, X = x, Y = y, Text = text, Size = size, Color = color, Align = align });
    }

    public static Rgba LiftColor(double twr) => twr < 1.0 ? Bad : twr < 1.25 ? Warn : Good;

    /// <summary>Sideways and braking: under 0.3 is sluggish, under 0.6 is fair.</summary>
    public static Rgba MoveColor(double twr) => twr < 0.3 ? Bad : twr < 0.6 ? Warn : Good;

    private static Rgba EnduranceColor(double seconds) => seconds < 60 ? Bad : seconds < 300 ? Warn : Text;

    public static string Twr(double value)
    {
        if (double.IsNaN(value) || value <= 0) return "0";
        if (value >= 100) return "99+";
        return value.ToString(value >= 10 ? "0.0" : "0.00", Invariant);
    }

    public static string Number(double value, int decimals)
    {
        return value.ToString(decimals == 0 ? "#,0" : "#,0." + new string('0', decimals), Invariant);
    }

    private static string Multiplier(double value) => Number(value, Math.Abs(value - Math.Round(value)) < 1e-3 ? 0 : 1);

    /// <summary>Kilograms, as the game shows mass: 850 kg, 18.1k kg, 182k kg, 1.25M kg.</summary>
    public static string Mass(double kg)
    {
        if (double.IsNaN(kg)) return "?";
        if (kg < 1000) return Number(kg, 0) + " kg";
        if (kg < 100000) return Number(kg / 1000.0, 1) + "k kg";
        if (kg < 1000000) return Number(kg / 1000.0, 0) + "k kg";
        return Number(kg / 1000000.0, 2) + "M kg";
    }

    public static string Volume(double m3)
    {
        double litres = m3 * 1000.0;
        if (litres < 100000) return Number(litres, 0) + " L";
        return Number(litres / 1000.0, 0) + " kL";
    }

    public static string Time(double seconds)
    {
        if (double.IsInfinity(seconds)) return "unlimited";
        if (double.IsNaN(seconds) || seconds < 0) return "?";
        if (seconds < 60) return Number(seconds, 0) + "s";
        if (seconds < 3600)
        {
            int m = (int)(seconds / 60);
            return m + "m " + ((int)seconds % 60).ToString("00", Invariant) + "s";
        }
        if (seconds < 360000)
        {
            int h = (int)(seconds / 3600);
            return h + "h " + ((int)(seconds / 60) % 60).ToString("00", Invariant) + "m";
        }
        return "99h+";
    }

    private static string Trim(string text, int max)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= max ? text : text.Substring(0, max - 1) + ".";
    }
}
