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
        /// <summary>
        /// The search field (also the window's rect), when <see cref="SearchFieldShown"/>: right of the portrait area when there
        /// is room, else (compact) in a row under the chip strip.
        /// </summary>
        public Rect Search;
        public bool SearchFieldShown;
        /// <summary>There is no room for the field beside the portraits (the alerts are at the right): a search icon stands in for it.</summary>
        public bool SearchCompact;
        public Rect SearchIcon;
        /// <summary>The "No matching colonists" line, in the place of the empty bar. Meaningful when <see cref="ShowEmpty"/>.</summary>
        public Rect Empty;
        public bool ShowEmpty;
        /// <summary>The row under the bar: the chips, or one Filters button when <see cref="StripCompact"/>.</summary>
        public Rect Strip;
        public bool StripCompact;
    }

    /// <summary>
    /// Pure geometry, so it can be asserted with synthetic bars: the search field sits right of the portraits when there is
    /// room, the chip row under everything the bar draws, and nothing overlaps a portrait, the vanilla alert column
    /// (154 px wide at the right edge, growing up to the top of the screen) or the screen's edge. The vanilla bar is at most
    /// <c>screenWidth - 520</c> wide and centered (260 px free on each side) and is hidden below 800x500; with the alerts
    /// taking 154 of those 260 px, a full bar leaves only about 90 px for the field, so the field collapses to a search icon
    /// whose opened field goes in a row under the chips (spec section 2, responsive behavior).
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
        /// <summary>Narrower than this the placeholder and the typed text no longer fit: the field collapses to an icon.</summary>
        public const float MinInlineSearchWidth = 170f;
        public const float IconSize = 24f;
        /// <summary>The vanilla alert column (<c>AlertsReadout</c>): 154 px at the right edge.</summary>
        public const float AlertsWidth = 154f;
        public const float MinScreenWidth = 800f, MinScreenHeight = 500f;

        public static Placement Compute(float screenWidth, float screenHeight, BarMetrics bar, bool showEmpty,
            float stripWidth, float filtersButtonWidth, float emptyWidth, bool searchOpen = false)
        {
            var p = new Placement { Visible = screenWidth >= MinScreenWidth && screenHeight >= MinScreenHeight };
            if (!p.Visible)
                return p;
            float center = screenWidth / 2f;
            float rightLimit = screenWidth - AlertsWidth - SideMargin;   // nothing of ours goes under the alert column

            // Search: right of the portrait area, level with the first row.
            float anchor = center + MinHalfBar;
            if (bar.HasPortraits && !showEmpty)
                anchor = Mathf.Max(anchor, bar.MaxX);
            float x = anchor + SideMargin;
            float span = rightLimit - x;
            float y = RowTop + (bar.HasPortraits && !showEmpty ? Mathf.Max(0f, (bar.PortraitHeight - ControlHeight) / 2f) : 0f);
            p.SearchCompact = span < MinInlineSearchWidth;
            if (!p.SearchCompact)
            {
                p.Search = new Rect(x, y, Mathf.Min(SearchWidth, span), ControlHeight);
                p.SearchFieldShown = true;
            }
            else
            {
                p.SearchIcon = new Rect(Mathf.Min(x, rightLimit - IconSize), y, IconSize, ControlHeight);
                p.SearchFieldShown = searchOpen;
            }

            // Empty state: in the place of the portraits, in the top row.
            p.ShowEmpty = showEmpty;
            if (showEmpty)
            {
                float w = Mathf.Min(emptyWidth, rightLimit - SideMargin);
                p.Empty = new Rect(Mathf.Clamp(center - w / 2f, SideMargin, rightLimit - w), RowTop, w, ControlHeight);
            }

            // Strip: under everything the bar draws (or under the empty-state line).
            float top = showEmpty || !bar.HasPortraits ? RowTop + ControlHeight : bar.BottomY;
            p.StripCompact = stripWidth > rightLimit - SideMargin;
            float sw = p.StripCompact ? Mathf.Min(filtersButtonWidth, rightLimit - SideMargin) : stripWidth;
            p.Strip = new Rect(Mathf.Clamp(center - sw / 2f, SideMargin, rightLimit - sw), top + Gap, sw, ControlHeight);
            if (p.SearchCompact && searchOpen)   // the opened field: a row of its own under the chips
                p.Search = new Rect(p.Strip.x, p.Strip.yMax + Gap, Mathf.Min(SearchWidth, rightLimit - p.Strip.x), ControlHeight);
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
