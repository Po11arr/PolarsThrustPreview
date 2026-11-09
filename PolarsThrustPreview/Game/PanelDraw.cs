using System.Collections.Generic;
using PolarsThrustPreview.Core;
using Sandbox.Graphics;
using VRage.Utils;
using VRageMath;

namespace PolarsThrustPreview.Game;

/// <summary>
/// Draws the prepared layout every frame: flat rectangles and text, nothing else. Panel pixels
/// are designed for a 1080p screen and scaled to the real resolution and the text size setting.
/// No allocation here.
/// </summary>
internal static class PanelDraw
{
    private const string Font = "White";
    private const string Blank = "Textures\\GUI\\Blank.dds";

    /// <summary>Text size setting that draws the panel at its designed size.</summary>
    private const float DesignTextScale = 0.8f;

    /// <summary>Font line height per design text size, so the glyphs match the mockup.</summary>
    private const float LineToFont = 1.2f;

    public static void Draw(List<DrawOp> ops, float panelHeight, float textScale, float positionX, float positionY)
    {
        if (ops.Count == 0 || panelHeight <= 0)
            return;

        // Normalized size of one screen pixel, and the scale from design pixels to screen pixels
        var perPixel = MyGuiManager.GetNormalizedSizeFromScreenSize(Vector2.One);
        if (perPixel.X <= 0 || perPixel.Y <= 0)
            return;
        float screenHeight = 1f / perPixel.Y;
        float k = screenHeight / 1080f * (textScale / DesignTextScale);
        var unit = new Vector2(k * perPixel.X, k * perPixel.Y);

        // Font height in screen pixels at scale 1, to size text in design pixels
        float fontPixels = MyGuiManager.GetFontHeight(Font, 1f) / perPixel.Y;
        if (fontPixels <= 0)
            return;

        var size = new Vector2(PanelLayout.Width * unit.X, panelHeight * unit.Y);
        var min = MyGuiManager.GetMinMouseCoord();
        var max = MyGuiManager.GetMaxMouseCoord() - size;
        var origin = new Vector2(
            MathHelper.Lerp(min.X, max.X, positionX),
            MathHelper.Lerp(min.Y, max.Y, positionY));

        for (int i = 0; i < ops.Count; i++)
        {
            var op = ops[i];
            var at = new Vector2(origin.X + op.X * unit.X, origin.Y + op.Y * unit.Y);
            var color = new Color(op.Color.R, op.Color.G, op.Color.B, op.Color.A);

            if (op.IsText)
            {
                float scale = op.Size * LineToFont * k / fontPixels;
                MyGuiManager.DrawString(Font, op.Text, at, scale, color, AlignOf(op.Align));
            }
            else if (op.W > 0 && op.H > 0)
            {
                MyGuiManager.DrawSpriteBatch(Blank, at, new Vector2(op.W * unit.X, op.H * unit.Y), color,
                    MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP, useFullClientArea: false, waitTillLoaded: false);
            }
        }
    }

    private static MyGuiDrawAlignEnum AlignOf(Align align)
    {
        switch (align)
        {
            case Align.Center: return MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER;
            case Align.Right: return MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER;
            default: return MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER;
        }
    }
}
