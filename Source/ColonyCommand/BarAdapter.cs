using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace ColonyCommand
{
    /// <summary>
    /// The only code that touches the vanilla <see cref="ColonistBar"/>, without Harmony. Vanilla hands out its private
    /// entry and draw-location lists by reference (<c>Entries</c>, <c>DrawLocs</c>), so after a vanilla recache the view
    /// is rewritten in place, laid out again with our own <see cref="ColonistBarDrawLocsFinder"/>, and the one private
    /// value that has no accessor (<c>cachedScale</c>) is set through reflection resolved once at startup.
    ///
    /// The view is <see cref="RosterView"/> applied to the vanilla entries with the user's <see cref="ViewState"/>; with
    /// nothing active the bar is not touched at all. Everything here is memory only: nothing is saved and no game data
    /// (pawns, selection, display order) is changed.
    /// </summary>
    public static class BarAdapter
    {
        // Shared by every entry of our view. Vanilla builds fresh per-entry closures on each recache, so "the list still
        // carries this exact delegate" means "nothing rebuilt it since we wrote it". It also keeps a drag from reaching
        // ColonistBar.Reorder, which writes playerSettings.displayOrder.
        private static readonly Action<int, int> NoReorder = (a, b) => { };
        private static readonly Action<int, Vector2> NoDragGhost = (index, dragStart) => { };

        private static FieldInfo scaleField;
        private static ColonistBarDrawLocsFinder drawLocsFinder;
        private static readonly List<ColonistBar.Entry> view = new List<ColonistBar.Entry>();
        private static bool applied;
        private static int appliedVersion, shown;
        private static RosterCounts counts;
        private static float nextRefresh;
        private static BarMetrics footprint;
        private static readonly List<Vector2> footprintLocs = new List<Vector2>();

        /// <summary>
        /// How often the view and the chip counts are re-evaluated against the live game (real time): a colonist drafted or
        /// injured changes no roster, so nothing else would tell the bar.
        /// </summary>
        public const float RefreshSeconds = 0.25f;

        /// <summary>False when the private member this relies on is missing (one logged error, then the bar stays vanilla).</summary>
        public static bool Enabled { get; private set; }

        /// <summary>
        /// Chip counts from the vanilla roster the current view was built from (spec §3.2: before the active status and
        /// skill criteria). Default (all zero) while no view has been built.
        /// </summary>
        public static RosterCounts Counts => counts;

        /// <summary>
        /// The footprint of the bar as vanilla lays it out unfiltered (measured from the vanilla roster at each build), while a
        /// view is written; default otherwise. The controls anchor to it so they do not move while the user filters.
        /// </summary>
        public static BarMetrics VanillaFootprint => footprint;

        /// <summary>Colonists (pawn entries) the bar shows right now: with nothing active, the eligible ones. 0 behind an empty state.</summary>
        public static int Shown => shown;

        /// <summary>Our view is currently written into the bar.</summary>
        public static bool Applied => applied;

        /// <summary><see cref="ViewState.Version"/> the written view was built for (meaningful while <see cref="Applied"/>).</summary>
        public static int AppliedVersion => appliedVersion;

        private const string ScaleFieldName = "cachedScale";

        /// <summary>Resolves the private field once. Fails closed: feature off and one error, never a per-frame exception.</summary>
        public static void Resolve() => Resolve(ScaleFieldName);

        private static void Resolve(string fieldName)
        {
            scaleField = typeof(ColonistBar).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (scaleField == null || scaleField.FieldType != typeof(float))
            {
                Disable($"ColonistBar.{fieldName} was not found (changed by a game update?): the colonist bar view stays off");
                return;
            }
            Enabled = true;
        }

        /// <summary>
        /// Brings the bar in line with the current view. Cheap when nothing changed. Called after the game's ticks
        /// (Bootstrap.LateUpdate), and from the window's ExtraOnGUI and WindowUpdate for changes made during OnGUI.
        /// </summary>
        public static void Apply()
        {
            // First, and whether or not the seam is enabled: a stale search must not survive into another game.
            ViewState.SyncGame();
            if (!Enabled)
                return;
            try
            {
                // The body is its own method so a member missing in another game version fails here, inside the try.
                ApplyCore();
            }
            catch (Exception e)
            {
                Disable($"the colonist bar view is switched off after an exception: {e}");
            }
        }

        private static void ApplyCore()
        {
            if (Current.ProgramState != ProgramState.Playing || !(Find.UIRoot is UIRoot_Play))
                return;
            ColonistBar bar = Find.ColonistBar;
            Criteria criteria = ViewState.Snapshot;
            int version = ViewState.Version;
            float now = Time.realtimeSinceStartup;
            bool refreshDue = now >= nextRefresh;
            if (criteria.IsIdentity)
            {
                if (applied)
                {
                    applied = false;
                    footprint = default;
                    bar.MarkColonistsDirty();   // vanilla rebuilds the roster and the layout on its next read
                    refreshDue = true;
                }
                if (refreshDue)
                {
                    nextRefresh = now + RefreshSeconds;
                    counts = RosterView.Count(bar.Entries, criteria.Query);   // the bar is vanilla: nothing of ours in the list
                    shown = counts.Eligible;
                }
                return;
            }
            // Built from this very request and not due for a look at the live game? Then only a vanilla rebuild (a list that
            // is no longer ours) needs repairing.
            bool upToDate = applied && appliedVersion == version && !refreshDue;
            if (!upToDate)
                bar.MarkColonistsDirty();   // start from a fresh vanilla cache (this also resets its reorder groups)
            List<ColonistBar.Entry> entries = bar.Entries;   // vanilla recaches here when it was marked dirty
            if (upToDate && IsView(entries))
                return;
            drawLocsFinder ??= new ColonistBarDrawLocsFinder();
            MeasureVanillaFootprint(entries);   // while the list is still vanilla's
            int groups = RosterView.Build(entries, criteria, view, out counts);
            shown = 0;
            for (int i = 0; i < view.Count; i++)
            {
                view[i] = Stamp(view[i]);
                if (view[i].pawn != null)
                    shown++;
            }
            nextRefresh = now + RefreshSeconds;
            entries.Clear();
            entries.AddRange(view);
            view.Clear();   // keep no pawn references between calls
            drawLocsFinder.CalculateDrawLocs(bar.DrawLocs, out float scale, groups);
            scaleField.SetValue(bar, scale);
            applied = true;
            appliedVersion = version;
        }

        /// <summary>Lays the vanilla entries out into scratch space and keeps what the unfiltered bar would occupy.</summary>
        private static void MeasureVanillaFootprint(List<ColonistBar.Entry> vanilla)
        {
            int groups = vanilla.Count == 0 ? 0 : vanilla[vanilla.Count - 1].group + 1;
            drawLocsFinder.CalculateDrawLocs(footprintLocs, out float scale, groups);   // reads the bar's current list: vanilla's
            footprint = ControlsLayout.Measure(footprintLocs, ColonistBar.BaseSize * scale, scale, groups);
            footprintLocs.Clear();
        }

        /// <summary>Marks an entry as ours (see <see cref="IsView"/>) and turns its drag into a no-op.</summary>
        private static ColonistBar.Entry Stamp(ColonistBar.Entry entry)
        {
            entry.reorderAction = NoReorder;
            entry.extraDraggedItemOnGUI = NoDragGhost;
            return entry;
        }

        /// <summary>True when every entry is one of ours (an empty list has nothing to hide, so it counts).</summary>
        private static bool IsView(List<ColonistBar.Entry> entries)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (!ReferenceEquals(entries[i].reorderAction, NoReorder))
                    return false;
            }
            return true;
        }

        /// <summary>Fails closed: logs once, stops touching the bar for good, and hands it back to vanilla.</summary>
        private static void Disable(string error)
        {
            Enabled = false;
            applied = false;
            counts = default;
            shown = 0;
            footprint = default;
            view.Clear();
            CC.Error(error);
            try
            {
                Find.ColonistBar.MarkColonistsDirty();   // vanilla rebuilds its own roster and layout on the next read
            }
            catch (Exception)
            {
                // no play UI yet (startup), nothing to restore
            }
        }
    }
}
