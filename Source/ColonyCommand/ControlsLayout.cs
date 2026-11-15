using UnityEngine;

namespace ColonyCommand
{
    /// <summary>
    /// Where the controls go this event. Rects are in screen units. The panel is one column at the right edge of the screen,
    /// as wide as vanilla's Learning Helper and right under it. It is laid out in rows of <see cref="ControlsLayout.RowHeight"/>:
    /// row 0 is the search field, 1 the All chip, 2 and 3 the other four chips in two columns, 4 the Skill (and Level) buttons,
    /// 5 Sort (and its direction), then Reset (only when something is set) and the empty-state line (only with no results).
    /// <see cref="Compact"/> is the fallback for a column with too little height: row 0 holds the search field and a Filters
    /// button (the same actions in a menu), and the empty-state line is row 1.
    /// </summary>
    public struct Placement
    {
        /// <summary>False below 800x500 (the colonist bar is hidden there) or with the bar switched off: nothing is drawn and nothing takes input.</summary>
        public bool Visible;
        /// <summary>Not enough height below the Learning Helper for the whole panel: a single row, search field plus Filters button.</summary>
        public bool Compact;
        /// <summary>The panel's background; also what swallows a click that lands between the controls.</summary>
        public Rect Panel;
        /// <summary>The search field (also the window's rect). Shown whenever <see cref="Visible"/>.</summary>
        public Rect Search;
        /// <summary>The Filters button of the compact layout (zero-sized otherwise).</summary>
        public Rect Filters;
        /// <summary>Top-left of row 0.</summary>
        public float ContentX, ContentY;
        /// <summary>Row of the Reset button and of the empty-state line, or -1 when it is not shown.</summary>
        public int ResetRow, EmptyRow;

        /// <summary>The whole width of the panel's content in this row.</summary>
        public Rect Row(int row) => new Rect(ContentX, ContentY + row * (ControlsLayout.RowHeight + ControlsLayout.RowGap), ControlsLayout.InnerWidth, ControlsLayout.RowHeight);

        /// <summary>The left (0) or right (1) half of a row.</summary>
        public Rect Cell(int row, int column)
        {
            Rect r = Row(row);
            float w = (r.width - ControlsLayout.Gap) / 2f;
            return new Rect(r.x + column * (w + ControlsLayout.Gap), r.y, w, r.height);
        }
    }

    /// <summary>
    /// Pure geometry, so it can be asserted over screen sizes and free heights. Vanilla's right edge is crowded: the Learning
    /// Helper at the top (200 px wide, 8 px from the edge, as tall as its content), and from the bottom up the weather and
    /// date, the letters and the alerts (154 px wide). The panel sits in the gap: it starts under the Learning Helper
    /// (<c>top</c>) and must end above the alerts (<c>bottom</c>), or whatever is below it would take its clicks. It never
    /// reaches the colonist bar: the bar is at most <c>screenWidth - 520</c> wide and centered, so it ends 260 px short of
    /// the right edge, and the panel starts 208 px short of it.
    /// </summary>
    public static class ControlsLayout
    {
        public const float PanelWidth = 200f;
        /// <summary>The gap to the screen's right edge: the Learning Helper's (<c>LearningReadout.OuterMargin</c>).</summary>
        public const float RightMargin = 8f;
        public const float Pad = 7f;
        public const float InnerWidth = PanelWidth - 2f * Pad;
        public const float RowHeight = 24f;
        public const float RowGap = 4f;
        public const float Gap = 4f;
        /// <summary>The rows of the whole panel when nothing is set: search, All, two chip rows, Skill, Sort.</summary>
        public const int BaseRows = 6;
        /// <summary>With the Reset row and the empty-state line as well: the tallest the panel gets.</summary>
        public const int MaxRows = BaseRows + 2;
        /// <summary>Free height a compact panel must regain before it goes back to the whole panel (no flicker when an alert comes and goes).</summary>
        public const float Hysteresis = 28f;
        public const float MinScreenWidth = 800f, MinScreenHeight = 500f;
        /// <summary>The narrowest the Filters button of the compact layout and the search field beside it may be.</summary>
        public const float MinFiltersWidth = 60f, MinCompactSearchWidth = 90f;

        /// <summary>Height of a panel with this many rows.</summary>
        public static float HeightOfRows(int rows) => 2f * Pad + rows * RowHeight + (rows - 1) * RowGap;

        /// <summary>
        /// <paramref name="top"/> and <paramref name="bottom"/> bound the free part of the right column (below the Learning
        /// Helper, above the alerts). Whether the panel is compact is decided from its tallest form, not from the rows shown
        /// now, so that Reset and the empty-state line appearing never fold it. <paramref name="wasCompact"/> adds hysteresis.
        /// </summary>
        public static Placement Compute(float screenWidth, float screenHeight, float top, float bottom, bool showReset, bool showEmpty,
            float filtersWidth, bool wasCompact = false)
        {
            var p = new Placement { Visible = screenWidth >= MinScreenWidth && screenHeight >= MinScreenHeight, ResetRow = -1, EmptyRow = -1 };
            if (!p.Visible)
                return p;
            p.Compact = bottom - top < HeightOfRows(MaxRows) + (wasCompact ? Hysteresis : 0f);
            int rows;
            if (p.Compact)
            {
                rows = showEmpty ? 2 : 1;
                p.EmptyRow = showEmpty ? 1 : -1;
            }
            else
            {
                rows = BaseRows;
                if (showReset)
                    p.ResetRow = rows++;
                if (showEmpty)
                    p.EmptyRow = rows++;
            }
            float height = HeightOfRows(rows);
            float x = screenWidth - RightMargin - PanelWidth;
            float y = Mathf.Max(0f, Mathf.Min(top, screenHeight - height));   // on screen however little room there is
            p.Panel = new Rect(x, y, PanelWidth, height);
            p.ContentX = x + Pad;
            p.ContentY = y + Pad;
            if (p.Compact)
            {
                float f = Mathf.Clamp(filtersWidth, MinFiltersWidth, InnerWidth - Gap - MinCompactSearchWidth);
                p.Search = new Rect(p.ContentX, p.ContentY, InnerWidth - Gap - f, RowHeight);
                p.Filters = new Rect(p.Search.xMax + Gap, p.ContentY, f, RowHeight);
            }
            else
            {
                p.Search = p.Row(0);
            }
            return p;
        }
    }
}
