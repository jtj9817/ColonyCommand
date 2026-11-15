using System.Collections.Generic;
using System.Linq;
using ColonyCommand;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ColonyCommandTests
{
    /// <summary>What the vanilla bar was about to draw or handle on one non-Layout OnGUI event.</summary>
    public struct DrawRecord
    {
        public int frame;
        public EventType type;
        /// <summary>The bar was marked dirty when the event reached it: it rebuilds the VANILLA roster now (a flash).</summary>
        public bool dirty;
        /// <summary>"group:ThingID" per entry, as the bar iterated them.</summary>
        public string entries;
        public int entryCount;
        public int locCount;

        public override string ToString() =>
            $"frame {frame} {type} dirty={dirty} entries={entryCount} locs={locCount} [{entries}]";
    }

    /// <summary>
    /// Test-only observer of the vanilla colonist bar (Harmony is allowed in this mod, never in the shipped one). It reads
    /// the bar's private state without calling its getters: <c>Entries</c> would itself recache and hide the very flash
    /// this exists to catch.
    /// </summary>
    public static class BarProbe
    {
        private static readonly AccessTools.FieldRef<ColonistBar, bool> DirtyRef =
            AccessTools.FieldRefAccess<ColonistBar, bool>("entriesDirty");
        private static readonly AccessTools.FieldRef<ColonistBar, List<ColonistBar.Entry>> EntriesRef =
            AccessTools.FieldRefAccess<ColonistBar, List<ColonistBar.Entry>>("cachedEntries");
        private static readonly AccessTools.FieldRef<ColonistBar, List<Vector2>> LocsRef =
            AccessTools.FieldRefAccess<ColonistBar, List<Vector2>>("cachedDrawLocs");
        private static readonly AccessTools.FieldRef<ColonistBar, float> ScaleRef =
            AccessTools.FieldRefAccess<ColonistBar, float>("cachedScale");

        public static readonly List<DrawRecord> Draws = new List<DrawRecord>();

        /// <summary>Record draws (and count ColonistBar.Reorder calls) while true.</summary>
        public static bool Recording;

        /// <summary>Calls to ColonistBar.Reorder, the vanilla writer of playerSettings.displayOrder.</summary>
        public static int ReorderCalls;

        /// <summary>Test hook: mark the bar dirty right after it handled each Repaint (a UI-originated dirty).</summary>
        public static bool DirtyAfterRepaint;

        /// <summary>Times the hook above fired (a control: the phase really exercised it).</summary>
        public static int DirtyFired;

        /// <summary>Test hooks that skip the mod's own apply calls, to prove the detector sees the flash they prevent.</summary>
        public static bool SuppressLateUpdate, SuppressWindowHooks;

        public static bool IsDirty(ColonistBar bar) => DirtyRef(bar);
        public static List<ColonistBar.Entry> CachedEntries(ColonistBar bar) => EntriesRef(bar);
        public static List<Vector2> CachedLocs(ColonistBar bar) => LocsRef(bar);
        public static float CachedScale(ColonistBar bar) => ScaleRef(bar);

        public static void Start()
        {
            Draws.Clear();
            ReorderCalls = 0;
            DirtyFired = 0;
            Recording = true;
        }

        public static void Stop()
        {
            Recording = false;
            DirtyAfterRepaint = false;
            SuppressLateUpdate = false;
            SuppressWindowHooks = false;
        }

        public static string Describe(IEnumerable<ColonistBar.Entry> entries) =>
            string.Join(",", entries.Select(e => $"{e.group}:{e.pawn?.ThingID ?? "-"}"));

        public static int Repaints => Draws.Count(d => d.type == EventType.Repaint);

        /// <summary>The bar ignores Layout events, and draws nothing below 800x500 (ColonistBar.Visible).</summary>
        private static bool Watching(out EventType type)
        {
            type = Event.current.type;
            return Recording && type != EventType.Layout && UI.screenWidth >= 800 && UI.screenHeight >= 500
                && !Find.TilePicker.Active;
        }

        private static bool dirtyAtStart;

        internal static void Before(ColonistBar bar)
        {
            dirtyAtStart = Watching(out _) && DirtyRef(bar);
        }

        internal static void After(ColonistBar bar)
        {
            if (!Watching(out EventType type))
                return;
            List<ColonistBar.Entry> entries = EntriesRef(bar);
            Draws.Add(new DrawRecord
            {
                frame = Time.frameCount,
                type = type,
                dirty = dirtyAtStart,
                entries = Describe(entries),
                entryCount = entries.Count,
                locCount = LocsRef(bar).Count,
            });
            if (DirtyAfterRepaint && type == EventType.Repaint)
            {
                DirtyFired++;
                bar.MarkColonistsDirty();
            }
        }

        /// <summary>Records that do not show exactly <paramref name="expected"/>, or were drawn dirty, or are out of sync.</summary>
        public static List<DrawRecord> Bad(string expected) =>
            Draws.Where(d => d.dirty || d.entries != expected || d.entryCount != d.locCount).ToList();
    }

    /// <summary>
    /// Catches the mod's own deliberate errors in a scenario that provokes them (the harness counts every
    /// "[ColonyCommand] ERROR" line as a failure). Only errors carrying the mod's tag are held back, and only while active.
    /// </summary>
    public static class ErrorCapture
    {
        public static bool Active;
        public static readonly List<string> Captured = new List<string>();

        public static void Start()
        {
            Captured.Clear();
            Active = true;
        }

        public static void Stop() => Active = false;
    }

    [HarmonyPatch(typeof(Log), nameof(Log.Error), new[] { typeof(string) })]
    public static class Patch_Log_Error
    {
        public static bool Prefix(string text)
        {
            if (!ErrorCapture.Active || text == null || !text.StartsWith($"{CC.Tag} ERROR"))
                return true;
            ErrorCapture.Captured.Add(text);
            return false;
        }
    }

    [HarmonyPatch(typeof(ColonistBar), nameof(ColonistBar.ColonistBarOnGUI))]
    public static class Patch_ColonistBar_OnGUI
    {
        public static void Prefix(ColonistBar __instance) => BarProbe.Before(__instance);

        public static void Postfix(ColonistBar __instance) => BarProbe.After(__instance);
    }

    [HarmonyPatch(typeof(ColonistBar), nameof(ColonistBar.Reorder))]
    public static class Patch_ColonistBar_Reorder
    {
        public static void Prefix()
        {
            if (BarProbe.Recording)
                BarProbe.ReorderCalls++;
        }
    }

    [HarmonyPatch(typeof(Bootstrap), "LateUpdate")]
    public static class Patch_Bootstrap_LateUpdate
    {
        public static bool Prefix() => !BarProbe.SuppressLateUpdate;
    }

    [HarmonyPatch(typeof(ColonyCommandWindow), nameof(ColonyCommandWindow.ExtraOnGUI))]
    public static class Patch_Window_ExtraOnGUI
    {
        public static bool Prefix() => !BarProbe.SuppressWindowHooks;
    }

    [HarmonyPatch(typeof(ColonyCommandWindow), nameof(ColonyCommandWindow.WindowUpdate))]
    public static class Patch_Window_WindowUpdate
    {
        public static bool Prefix() => !BarProbe.SuppressWindowHooks;
    }
}
