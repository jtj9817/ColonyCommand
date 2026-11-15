using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ColonyCommand
{
    /// <summary>
    /// The mod's own look, chosen to read for every kind of colour vision and to stand out from the game's browns and greens:
    /// a deep navy panel with a sky-blue edge, orange for "selected", yellow for the one warning. Blue, orange and yellow are
    /// three of the Okabe-Ito colour-blind-safe hues, and they differ in brightness as well as hue (a selected button is
    /// bright with dark text, every other one dark with light text), so nothing depends on telling two hues apart. A selected
    /// button also carries a marker bar, a shape cue that survives grayscale. <c>tools/palette-check.py</c> reads the colours
    /// below and checks their contrast and their distinctness under protan, deutan and tritan simulation: rerun it after
    /// changing one. Keep each colour on one <c>Hex(...)</c> line: that is what it parses.
    /// </summary>
    public static class Look
    {
        // The panel.
        public static readonly Color PanelFill = Hex(0x0A1724, 0.96f);
        public static readonly Color PanelBorder = Hex(0x7CC6F2);
        // A button at rest, under the mouse, and selected (the criterion is set). Text and marker bar sit on top of these.
        public static readonly Color ButtonFill = Hex(0x1B3550);
        public static readonly Color ButtonBorder = Hex(0x6FA8D6);
        public static readonly Color ButtonHoverFill = Hex(0x254669);
        public static readonly Color ButtonHoverBorder = Hex(0xBFE3F8);
        public static readonly Color SelectedFill = Hex(0xE69F00);
        public static readonly Color SelectedBorder = Hex(0xFFE2A0);
        public static readonly Color SelectedHoverFill = Hex(0xF7B733);
        public static readonly Color TextOnButton = Hex(0xF5F8FB);
        public static readonly Color TextOnSelected = Hex(0x0B0B0B);
        // The "No matching colonists" line: yellow, not red (red is the hue most colour-blind players lose).
        public static readonly Color Warning = Hex(0xF0E442);

        private static Color Hex(int rgb, float alpha = 1f) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);

        /// <summary>The panel's background and edge.</summary>
        public static void DrawPanel(Rect rect) => Widgets.DrawBoxSolidWithOutline(rect, PanelFill, PanelBorder, 2);

        /// <summary>
        /// A button. <paramref name="selected"/> marks a criterion that is set: bright fill, dark text and a marker bar. Draws
        /// with the current text font and returns true on a click (consumed, so the map under it never sees it).
        /// </summary>
        public static bool Button(Rect rect, string label, string tip, bool selected)
        {
            bool over = Mouse.IsOver(rect);
            Color fill = selected ? (over ? SelectedHoverFill : SelectedFill) : (over ? ButtonHoverFill : ButtonFill);
            Color border = selected ? SelectedBorder : (over ? ButtonHoverBorder : ButtonBorder);
            Widgets.DrawBoxSolidWithOutline(rect, fill, border);
            Color text = selected ? TextOnSelected : TextOnButton;
            if (selected)
                Widgets.DrawBoxSolid(new Rect(rect.x + 4f, rect.y + 5f, 3f, rect.height - 10f), text);
            DrawLabel(rect, label, text);
            TooltipHandler.TipRegion(rect, tip);
            return Widgets.ButtonInvisible(rect);
        }

        /// <summary>A centered label in <paramref name="color"/>, cut to fit with an ellipsis.</summary>
        public static void DrawLabel(Rect rect, string label, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            using (new TextBlock(GameFont.Small, TextAnchor.MiddleCenter))
                Widgets.Label(rect, Cut(label, rect.width - 18f));
            GUI.color = previous;
        }

        // GenText.Truncate builds new strings every call when the text does not fit, and a label is drawn on every OnGUI event.
        private struct CutKey : IEquatable<CutKey>
        {
            public string Text;
            public int Width;
            public bool Equals(CutKey other) => Width == other.Width && string.Equals(Text, other.Text, StringComparison.Ordinal);
            public override bool Equals(object obj) => obj is CutKey other && Equals(other);
            public override int GetHashCode() => (Text == null ? 0 : Text.GetHashCode()) * 31 + Width;
        }

        private static readonly Dictionary<CutKey, string> cuts = new Dictionary<CutKey, string>();

        private static string Cut(string label, float width)
        {
            var key = new CutKey { Text = label, Width = (int)width };
            if (!cuts.TryGetValue(key, out string cut))
            {
                if (cuts.Count > 256)
                    cuts.Clear();   // a label that carries a count changes now and then: keep the cache small
                cut = label.Truncate(width);
                cuts[key] = cut;
            }
            return cut;
        }
    }
}
