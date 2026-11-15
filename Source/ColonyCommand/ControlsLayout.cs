using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ColonyCommand
{
    /// <summary>What the layout needs to know about the bar's portraits, in screen units.</summary>
    public struct BarMetrics
    {
        /// <summary>The bar draws at least one portrait.</summary>
        public bool HasPortraits;
        /// <summary>Horizontal extent of the portraits, group frames included.</summary>
        public float MinX, MaxX;
        /// <summary>Everything the bar draws under its portraits (labels, group frame, a weapon icon) ends above this y.</summary>
        public float BottomY;
        /// <summary>Height of one portrait (<c>ColonistBar.Size.y</c>).</summary>
        public float PortraitHeight;
    }

    /// <summary>Where the controls go this event. Rects are in screen units (the bar's own coordinates).</summary>
    public struct Placement
    {
        /// <summary>False below 800x500, with the bar hidden: nothing is drawn and nothing takes input.</summary>
        public bool Visible;
        /// <summary>The search field, right of the portrait area (also the window's rect).</summary>
        public Rect Search;
        /// <summary>The "No matching colonists" line, in the place of the empty bar. Meaningful when <see cref="ShowEmpty"/>.</summary>
        public Rect Empty;
        public bool ShowEmpty;
        /// <summary>The row under the bar: the chips, or one Filters button when <see cref="StripCompact"/>.</summary>
        public Rect Strip;
        public bool StripCompact;
    }

    /// <summary>
    /// Pure geometry, so it can be asserted with synthetic bars: the search field sits right of the portraits, the chip row
    /// under everything the bar draws, and neither ever overlaps a portrait or leaves the screen. The vanilla bar is at most
    /// <c>screenWidth - 520</c> wide and centered (260 px free on each side), and is hidden below 800x500, so on every
    /// screen where the controls show there is room for the field to the right of the bar.
    /// </summary>
    public static class ControlsLayout
    {
        /// <summary>The bar's top margin (<c>ColonistBarDrawLocsFinder.MarginTop</c>).</summary>
        public const float RowTop = 21f;
        public const float ControlHeight = 24f;
        public const float Gap = 4f;
        public const float SideMargin = 8f;
        /// <summary>The narrowest bar the layout is built around: the controls never come closer than this to the middle.</summary>
        public const float MinHalfBar = 140f;
        public const float SearchWidth = 220f;
        public const float MinSearchWidth = 120f;
        public const float MinScreenWidth = 800f, MinScreenHeight = 500f;

        public static Placement Compute(float screenWidth, float screenHeight, BarMetrics bar, bool showEmpty,
            float stripWidth, float filtersButtonWidth, float emptyWidth)
        {
            var p = new Placement { Visible = screenWidth >= MinScreenWidth && screenHeight >= MinScreenHeight };
            if (!p.Visible)
                return p;
            float center = screenWidth / 2f;

            // Search: right of the portrait area, level with the first row.
            float anchor = center + MinHalfBar;
            if (bar.HasPortraits && !showEmpty)
                anchor = Mathf.Max(anchor, bar.MaxX);
            float x = anchor + SideMargin;
            float width = Mathf.Min(SearchWidth, screenWidth - SideMargin - x);
            if (width < MinSearchWidth)
            {
                width = MinSearchWidth;
                x = screenWidth - SideMargin - width;
            }
            float y = RowTop + (bar.HasPortraits && !showEmpty ? Mathf.Max(0f, (bar.PortraitHeight - ControlHeight) / 2f) : 0f);
            p.Search = new Rect(x, y, width, ControlHeight);

            // Empty state: in the place of the portraits, in the top row.
            p.ShowEmpty = showEmpty;
            if (showEmpty)
            {
                float w = Mathf.Min(emptyWidth, screenWidth - 2f * SideMargin);
                p.Empty = new Rect(Mathf.Clamp(center - w / 2f, SideMargin, screenWidth - SideMargin - w), RowTop, w, ControlHeight);
            }

            // Strip: under everything the bar draws (or under the empty-state line).
            float top = showEmpty || !bar.HasPortraits ? RowTop + ControlHeight : bar.BottomY;
            p.StripCompact = stripWidth > screenWidth - 2f * SideMargin;
            float sw = p.StripCompact ? Mathf.Min(filtersButtonWidth, screenWidth - 2f * SideMargin) : stripWidth;
            p.Strip = new Rect(Mathf.Clamp(center - sw / 2f, SideMargin, screenWidth - SideMargin - sw), top + Gap, sw, ControlHeight);
            return p;
        }

        /// <summary>Reads the portrait extent from the vanilla bar (draw locations, size, scale, group count), without recaching it.</summary>
        public static BarMetrics Measure(ColonistBar bar, int groupCount)
        {
            List<Vector2> locs = bar.DrawLocs;
            var m = new BarMetrics { HasPortraits = locs.Count > 0, PortraitHeight = bar.Size.y };
            if (!m.HasPortraits)
                return m;
            Vector2 size = bar.Size;
            float scale = bar.Scale;
            float minX = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < locs.Count; i++)
            {
                minX = Mathf.Min(minX, locs[i].x);
                maxX = Mathf.Max(maxX, locs[i].x + size.x);
                maxY = Mathf.Max(maxY, locs[i].y);
            }
            float frame = groupCount >= 2 ? 12f * scale : 0f;   // the group frame grows 12 * scale around its portraits
            m.MinX = minX - frame;
            m.MaxX = maxX + frame;
            // Under a portrait: its name label (at least about 20 px of text), then possibly the group frame; a weapon icon hangs
            // at 1.05 * the portrait height and is 0.75 of it.
            float below = Mathf.Max(20f, 32f * scale, frame);
            m.BottomY = maxY + size.y + below;
            if (Prefs.ShowWeaponsUnderPortraitMode != ShowWeaponsUnderPortraitMode.Never)
                m.BottomY = Mathf.Max(m.BottomY, maxY + size.y * 1.95f);
            return m;
        }
    }
}
