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

namespace ColonyCommandTests
{
    /// <summary>
    /// Records the keyboard and mouse events the game's OnGUI actually receives (seen at the top of the mod window's
    /// <c>ExtraOnGUI</c>, i.e. before the controls handle them): whether xdotool's input arrived at all is the first question
    /// when a real-input scenario fails.
    /// </summary>
    public static class InputProbe
    {
        public static readonly System.Collections.Generic.List<string> Events = new System.Collections.Generic.List<string>();

        public static void Clear()
        {
            Events.Clear();
            RootCalls.Clear();
            WinCalls.Clear();
        }

        /// <summary>Calls by event type at the two points of the chain, to show whether the window stack was reached at all.</summary>
        public static readonly System.Collections.Generic.Dictionary<string, int> RootCalls = new System.Collections.Generic.Dictionary<string, int>();
        public static readonly System.Collections.Generic.Dictionary<string, int> WinCalls = new System.Collections.Generic.Dictionary<string, int>();

        private static void Count(System.Collections.Generic.Dictionary<string, int> d, string key)
        {
            d.TryGetValue(key, out int n);
            d[key] = n + 1;
        }

        public static string Tally() =>
            "root{" + string.Join(",", RootCalls.Select(kv => $"{kv.Key}:{kv.Value}")) + "} win{" + string.Join(",", WinCalls.Select(kv => $"{kv.Key}:{kv.Value}")) + "}";

        /// <summary>
        /// <paramref name="where"/> says at which point of the OnGUI chain the event was seen ("root" = the start of
        /// <c>UIRoot_Play.UIRootOnGUI</c>, "win" = the top of the mod window's <c>ExtraOnGUI</c>). The raw type survives a
        /// <c>Use()</c>, so an event consumed between the two still shows up at "win" as <c>raw=MouseDown type=Used</c>.
        /// </summary>
        internal static void Record(string where)
        {
            Event e = Event.current;
            EventType raw = e.rawType;
            Count(where == "root" ? RootCalls : WinCalls, raw.ToString());
            if (raw == EventType.Layout || raw == EventType.Repaint || Events.Count > 300)
                return;
            if (raw == EventType.KeyDown || raw == EventType.KeyUp)
                Events.Add($"f{Time.frameCount} {where} raw={raw} type={e.type} {e.keyCode}");
            else if (raw == EventType.MouseDown || raw == EventType.MouseUp)
                Events.Add($"f{Time.frameCount} {where} raw={raw} type={e.type} b{e.button} ({e.mousePosition.x:F0},{e.mousePosition.y:F0})");
        }

        public static string Describe() => string.Join(" | ", Events) + " " + Tally();
    }

    [HarmonyPatch(typeof(ColonyCommandWindow), nameof(ColonyCommandWindow.ExtraOnGUI))]
    public static class Patch_Window_ExtraOnGUI_Input
    {
        public static void Prefix() => InputProbe.Record("win");
    }

    [HarmonyPatch(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI))]
    public static class Patch_UIRootPlay_OnGUI_Input
    {
        public static void Prefix() => InputProbe.Record("root");
    }
}

namespace ColonyCommandTests
{
    /// <summary>
    /// Counts requests for the main menu tab. The harness closes every window that pauses the game within a frame, so "is the
    /// menu open" cannot be polled after Escape; the request itself (what <c>UIRoot_Play.OpenMainMenuShortcut</c> makes) can.
    /// </summary>
    public static class MenuRequests
    {
        public static int Count;
    }

    [HarmonyPatch(typeof(MainTabsRoot), nameof(MainTabsRoot.ToggleTab))]
    public static class Patch_MainTabsRoot_ToggleTab
    {
        public static void Prefix(MainButtonDef newTab)
        {
            if (newTab == MainButtonDefOf.Menu)
                MenuRequests.Count++;
        }
    }
}
