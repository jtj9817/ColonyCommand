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
    /// nothing active the bar is not touched at all. The Phase 1 probe (hide every other colonist) is kept as an internal
    /// test hook until the seam scenarios run against the real view. Everything here is memory only: nothing is saved
    /// and no game data (pawns, selection, display order) is changed.
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
        private static bool applied, appliedProbe;
        private static int appliedVersion;
        private static RosterCounts counts;

        /// <summary>False when the private member this relies on is missing (one logged error, then the bar stays vanilla).</summary>
        public static bool Enabled { get; private set; }

        /// <summary>
        /// Chip counts from the vanilla roster the current view was built from (spec §3.2: before the active status and
        /// skill criteria). Default (all zero) while no view has been built.
        /// </summary>
        public static RosterCounts Counts => counts;

        /// <summary>Test hook, kept until the seam scenarios move to the real view: hide every other colonist of each group, whatever the criteria. Memory only, off by default.</summary>
        public static bool ProbeView { get; set; }

        /// <summary>Our view is currently written into the bar.</summary>
        public static bool Applied => applied;

        /// <summary>The probe's rule: keep the colonists at even positions of their group (vanilla order), hide the rest.</summary>
        public static bool ProbeKeeps(int indexInGroup) => indexInGroup % 2 == 0;

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
            bool probe = ProbeView;
            if (!probe && criteria.IsIdentity)
            {
                if (applied)
                {
                    applied = false;
                    counts = default;
                    bar.MarkColonistsDirty();   // vanilla rebuilds the roster and the layout on its next read
                }
                return;
            }
            // Built from this very request? Then only a vanilla rebuild (a list that is no longer ours) needs repairing.
            bool upToDate = applied && appliedProbe == probe && appliedVersion == version;
            if (!upToDate)
                bar.MarkColonistsDirty();   // start from a fresh vanilla cache (this also resets its reorder groups)
            List<ColonistBar.Entry> entries = bar.Entries;   // vanilla recaches here when it was marked dirty
            if (upToDate && IsView(entries))
                return;
            int groups;
            if (probe)
            {
                groups = BuildProbe(entries);
                counts = default;
            }
            else
            {
                groups = RosterView.Build(entries, criteria, view, out counts);
                for (int i = 0; i < view.Count; i++)
                    view[i] = Stamp(view[i]);
            }
            entries.Clear();
            entries.AddRange(view);
            view.Clear();   // keep no pawn references between calls
            drawLocsFinder ??= new ColonistBarDrawLocsFinder();
            drawLocsFinder.CalculateDrawLocs(bar.DrawLocs, out float scale, groups);
            scaleField.SetValue(bar, scale);
            applied = true;
            appliedProbe = probe;
            appliedVersion = version;
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

        /// <summary>
        /// Fills <see cref="view"/> with the Phase 1 probe's view and returns the group count. Groups that lose every
        /// entry vanish and the rest are renumbered from 0: the layout indexes its per-group arrays by group id.
        /// </summary>
        private static int BuildProbe(List<ColonistBar.Entry> source)
        {
            view.Clear();
            int sourceGroup = -1, position = 0, lastKeptSource = -1, group = -1;
            for (int i = 0; i < source.Count; i++)
            {
                ColonistBar.Entry entry = source[i];
                if (entry.group != sourceGroup)
                {
                    sourceGroup = entry.group;
                    position = 0;
                }
                if (entry.pawn != null && !ProbeKeeps(position++))
                    continue;
                if (entry.group != lastKeptSource)
                {
                    lastKeptSource = entry.group;
                    group++;
                }
                view.Add(Stamp(new ColonistBar.Entry(entry.pawn, entry.map, group)));
            }
            return group + 1;
        }

        /// <summary>Fails closed: logs once, stops touching the bar for good, and hands it back to vanilla.</summary>
        private static void Disable(string error)
        {
            Enabled = false;
            applied = false;
            counts = default;
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
