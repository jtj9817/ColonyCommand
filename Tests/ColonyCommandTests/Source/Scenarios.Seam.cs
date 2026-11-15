using System;
using System.Collections.Generic;
using System.Linq;
using ColonyCommand;
using RimWorld;
using UnityEngine;
using Verse;

namespace ColonyCommandTests
{
    // Phase 1: does the zero-dependency seam hold? Every scenario below turns the probe view on (hide every other
    // colonist) and attacks the way it could break. The probe (BarProbe) reads what the vanilla bar actually iterates on
    // each OnGUI event, so "no flash" is judged on what was drawn, not on what the mod believes it wrote.
    public static partial class Scenarios
    {
        /// <summary>What a Phase 1 scenario knows about the roster it works with.</summary>
        private sealed class Roster
        {
            public List<ColonistBar.Entry> Vanilla;
            public string VanillaText, Expected;
            public float VanillaScale;
            public string Last = "";
            public readonly List<string> Notes = new List<string>();
        }

        private static ColonistBar Bar => Find.ColonistBar;

        // ----------------------------------------------------------------------------------------- shared steps

        /// <summary>Colonists-only fixtures, probe off, vanilla roster captured. The scenario leaves the probe off.</summary>
        private static Roster Prelude(Scenario s, int colonists)
        {
            var r = new Roster();
            Fixtures.EnsureColonists(s, colonists);
            s.OnFinish(() =>
            {
                BarAdapter.ProbeView = false;
                BarProbe.Stop();
            }).Summary(() => string.Join("; ", r.Notes.Append(r.Last).Where(x => x.Length > 0)));
            CaptureSteps(s, r, colonists);
            return r;
        }

        private static void CaptureSteps(Scenario s, Roster r, int pawns)
        {
            s.Do("probe off", () => BarAdapter.ProbeView = false)
                .ExpectWithin($"the vanilla roster of {pawns} colonists is captured", 20, () => CaptureVanilla(r, pawns), () => r.Last);
        }

        private static void ViewOnSteps(Scenario s, Roster r)
        {
            s.Do("probe on", () =>
                {
                    BarProbe.Start();
                    BarAdapter.ProbeView = true;
                })
                .ExpectWithin("the bar holds exactly the expected view", 20, () => ViewInPlace(r), () => r.Last);
        }

        // ------------------------------------------------------------------------------------------- the oracle

        /// <summary>The probe rule (even positions of each group stay), restated independently of BarAdapter.</summary>
        private static string ExpectedView(List<ColonistBar.Entry> vanilla)
        {
            var parts = new List<string>();
            int sourceGroup = -1, position = 0, lastKept = -1, group = -1;
            foreach (ColonistBar.Entry e in vanilla)
            {
                if (e.group != sourceGroup)
                {
                    sourceGroup = e.group;
                    position = 0;
                }
                if (e.pawn != null && !BarAdapter.ProbeKeeps(position++))
                    continue;
                if (e.group != lastKept)
                {
                    lastKept = e.group;
                    group++;
                }
                parts.Add($"{group}:{e.pawn?.ThingID ?? "-"}");
            }
            return string.Join(",", parts);
        }

        private static bool CaptureVanilla(Roster r, int pawns)
        {
            if (BarAdapter.Applied)
            {
                r.Last = "our view is still written";
                return false;
            }
            List<ColonistBar.Entry> entries = Bar.Entries;   // vanilla: nothing rewrites it now
            int count = entries.Count(e => e.pawn != null);
            if (count < pawns)
            {
                r.Last = $"the vanilla roster has {count} colonists, need {pawns}";
                return false;
            }
            r.Vanilla = new List<ColonistBar.Entry>(entries);
            r.VanillaText = BarProbe.Describe(r.Vanilla);
            r.Expected = ExpectedView(r.Vanilla);
            r.VanillaScale = BarProbe.CachedScale(Bar);
            return true;
        }

        /// <summary>Not dirty, holding exactly the expected entries, laid out consistently. Waits while a repair is pending.</summary>
        private static bool ViewInPlace(Roster r)
        {
            ColonistBar bar = Bar;
            if (BarProbe.IsDirty(bar))
            {
                r.Last = "the bar is dirty (repair pending)";
                return false;
            }
            List<ColonistBar.Entry> cached = BarProbe.CachedEntries(bar);
            string text = BarProbe.Describe(cached);
            if (text != r.Expected)
            {
                r.Last = $"the bar holds [{text}], expected [{r.Expected}]";
                return false;
            }
            string problem = LayoutProblem(bar, cached.Count);
            if (problem != null)
                throw new InvalidOperationException(problem);
            return true;
        }

        private static bool VanillaBack(Roster r)
        {
            ColonistBar bar = Bar;
            if (BarAdapter.Applied)
            {
                r.Last = "our view is still written";
                return false;
            }
            List<ColonistBar.Entry> entries = bar.Entries;   // vanilla again: reading it recaches, which is what we wait for
            string text = BarProbe.Describe(entries);
            if (text != r.VanillaText)
            {
                r.Last = $"the bar holds [{text}], vanilla is [{r.VanillaText}]";
                return false;
            }
            string problem = LayoutProblem(bar, entries.Count);
            if (problem != null)
                throw new InvalidOperationException(problem);
            float scale = BarProbe.CachedScale(bar);
            if (Mathf.Abs(scale - r.VanillaScale) > 0.001f)
            {
                r.Last = $"scale {scale}, vanilla {r.VanillaScale}";
                return false;
            }
            return true;
        }

        /// <summary>One draw location per entry, sane scale, no two portraits on top of each other, on screen.</summary>
        private static string LayoutProblem(ColonistBar bar, int entryCount)
        {
            List<Vector2> locs = BarProbe.CachedLocs(bar);
            if (locs.Count != entryCount)
                return $"{locs.Count} draw locations for {entryCount} entries (the bar indexes one into the other)";
            float scale = BarProbe.CachedScale(bar);
            if (!(scale > 0f && scale <= 1f))
                return $"scale {scale} is not in (0, 1]";
            Vector2 size = bar.Size;
            var seen = new HashSet<Vector2>();
            foreach (Vector2 loc in locs)
            {
                if (!seen.Add(loc))
                    return $"two portraits share the draw location {loc}";
                if (loc.x < 0f || loc.x + size.x > UI.screenWidth || loc.y < 0f || loc.y + size.y > UI.screenHeight / 2f)
                    return $"the portrait at {loc} (size {size}) is not on screen";
            }
            return null;
        }

        private static void AssertDraws(string phase, string expected, int minRepaints, bool ignoreDirty = false)
        {
            if (BarProbe.Repaints < minRepaints)
                throw new InvalidOperationException(
                    $"{phase}: only {BarProbe.Repaints} repaints observed, need {minRepaints}: the probe is not seeing the bar");
            List<DrawRecord> bad = BarProbe.Draws
                .Where(d => (!ignoreDirty && d.dirty) || d.entries != expected || d.entryCount != d.locCount).ToList();
            if (bad.Count > 0)
                throw new InvalidOperationException($"{phase}: {bad.Count} of {BarProbe.Draws.Count} draws were wrong, first: {bad[0]}");
        }

        // ----------------------------------------------------------------------------------------------- scenarios

        // Precondition: quicktest map, 5 colonists, probe off. Trigger: probe on, then off.
        // Failure modes: the view is not written, or written inconsistently (draw locations not matching entries make the
        // vanilla draw loop throw an IndexOutOfRange), portraits keep the vanilla layout, a hidden colonist is drawn, the
        // vanilla roster does not come back, or what is drawn differs from what the lists say.
        private static void SeamView()
        {
            var s = new Scenario("Seam view", 400000);
            Roster r = Prelude(s, 5);
            ViewOnSteps(s, r);
            s.Do("the relayout reached the bar: portraits are bigger than in the vanilla layout", () =>
                {
                    float scale = BarProbe.CachedScale(Bar);
                    r.Notes.Add($"scale {r.VanillaScale:F2} -> {scale:F2}");
                    if (r.VanillaScale < 0.999f && !(scale > r.VanillaScale))
                        throw new InvalidOperationException($"scale {scale} is not above the vanilla {r.VanillaScale} although fewer portraits are shown");
                })
                .ExpectWithin("20 repaints drawn", 30, () => BarProbe.Repaints >= 20, () => $"{BarProbe.Repaints} so far")
                .Do("every draw showed exactly the view", () => AssertDraws("view on", r.Expected, 20))
                .Do("probe off", () =>
                {
                    BarProbe.Start();
                    BarAdapter.ProbeView = false;
                })
                .ExpectWithin("the vanilla roster, order and layout are back", 20, () => VanillaBack(r), () => r.Last)
                .ExpectWithin("20 repaints drawn", 30, () => BarProbe.Repaints >= 20, () => $"{BarProbe.Repaints} so far")
                // The first draw after switching off rebuilds the cache itself (that is how vanilla is restored), so a
                // dirty flag is expected there; what must hold is that only the vanilla roster is ever drawn.
                .Do("every draw showed exactly the vanilla roster", () => AssertDraws("view off", r.VanillaText, 20, ignoreDirty: true))
                .Start();
        }

        // Precondition: view in place. Trigger: the bar is marked dirty every frame, as the game's ticks do.
        // Failure mode: a flash of the full roster (or a click handled on it): the bar rebuilds the VANILLA roster when it
        // is dirty at the start of an OnGUI event. The control phase removes every apply hook and must see that flash, so a
        // clean result elsewhere cannot be a blind detector.
        private static void SeamDirty()
        {
            var s = new Scenario("Seam dirty", 600000);
            Roster r = Prelude(s, 5);
            ViewOnSteps(s, r);
            DirtyPhase(s, r, "control, no hooks", 40, lateUpdate: false, windowHooks: false, dirtyInUpdate: true, dirtyAfterRepaint: false, bad =>
            {
                if (bad.Count == 0)
                    throw new InvalidOperationException("the detector saw no flash although every apply hook was removed: a clean result in the other phases would prove nothing");
            });
            DirtyPhase(s, r, "LateUpdate only", 60, lateUpdate: true, windowHooks: false, dirtyInUpdate: true, dirtyAfterRepaint: false, bad =>
            {
                if (bad.Count > 0)
                    throw new InvalidOperationException($"{bad.Count} draws were wrong with only the LateUpdate hook, first: {bad[0]}");
            });
            // Observation for the plan's [verify] item, not a gate: does the window's ExtraOnGUI alone (Layout event) also do it?
            DirtyPhase(s, r, "window hooks only (observed)", 60, lateUpdate: false, windowHooks: true, dirtyInUpdate: true, dirtyAfterRepaint: false, bad => { });
            DirtyPhase(s, r, "all hooks, dirty from Update and from OnGUI", 60, lateUpdate: true, windowHooks: true, dirtyInUpdate: true, dirtyAfterRepaint: true, bad =>
            {
                if (BarProbe.DirtyFired < 10)
                    throw new InvalidOperationException($"the OnGUI dirty hook fired only {BarProbe.DirtyFired} times");
                if (bad.Count > 0)
                    throw new InvalidOperationException($"{bad.Count} draws were wrong with every hook active, first: {bad[0]}");
            });
            s.Start();
        }

        private static void DirtyPhase(Scenario s, Roster r, string name, int frames, bool lateUpdate, bool windowHooks,
            bool dirtyInUpdate, bool dirtyAfterRepaint, Action<List<DrawRecord>> check)
        {
            int n = 0;
            s.Do($"{name}: start", () =>
                {
                    BarProbe.Start();
                    BarProbe.SuppressLateUpdate = !lateUpdate;
                    BarProbe.SuppressWindowHooks = !windowHooks;
                    BarProbe.DirtyAfterRepaint = dirtyAfterRepaint;
                    n = 0;
                })
                .Expect($"{name}: {frames} frames", () =>
                {
                    if (dirtyInUpdate)
                        Bar.MarkColonistsDirty();
                    return ++n >= frames;
                })
                .Do($"{name}: check", () =>
                {
                    List<DrawRecord> bad = BarProbe.Bad(r.Expected);
                    r.Notes.Add($"{name}: {bad.Count}/{BarProbe.Draws.Count} draws wrong");
                    int fired = BarProbe.DirtyFired;
                    BarProbe.Stop();   // also lifts the suppression
                    BarProbe.DirtyFired = fired;
                    check(bad);
                })
                .ExpectWithin($"{name}: the view is back in place", 20, () => ViewInPlace(r), () => r.Last);
        }

        // Precondition: view in place, hidden colonists exist. Trigger: hit tests over the whole top band, then real mouse
        // clicks on each visible portrait while the bar is marked dirty every frame.
        // Failure mode: a click or a targeter resolves to a hidden colonist or to nothing (hit test and draw locations out
        // of step), or a click is handled on the vanilla roster for one frame after a dirty.
        private static void SeamHitTest()
        {
            var s = new Scenario("Seam hit test", 600000);
            Roster r = Prelude(s, 5);
            ViewOnSteps(s, r);
            s.Do("hit tests: every slot centre is its own colonist, hidden colonists are unreachable", () => HitSweep(r));
            int slots = 0;
            s.Do("count the visible portraits", () => slots = BarProbe.CachedEntries(Bar).Count);
            for (int slot = 0; slot < 3; slot++)
                ClickStep(s, slot);
            s.Do("real input reached the bar and every event saw the view", () =>
            {
                if (!BarProbe.Draws.Any(d => d.type == EventType.MouseDown))
                    throw new InvalidOperationException("the probe saw no MouseDown: the clicks did not reach the game (control)");
                r.Notes.Add($"{BarProbe.Draws.Count} events seen ({BarProbe.Draws.Count(d => d.type == EventType.MouseDown)} MouseDown), {slots} slots");
                AssertDraws("clicks", r.Expected, 5);
            });
            s.Start();
        }

        private static void HitSweep(Roster r)
        {
            ColonistBar bar = Bar;
            List<ColonistBar.Entry> entries = BarProbe.CachedEntries(bar);
            List<Vector2> locs = BarProbe.CachedLocs(bar);
            Vector2 size = bar.Size;
            var visible = new HashSet<Pawn>(entries.Where(e => e.pawn != null).Select(e => e.pawn));
            for (int i = 0; i < entries.Count; i++)
            {
                Thing at = bar.ColonistOrCorpseAt(locs[i] + size / 2f);
                if (at != entries[i].pawn)
                    throw new InvalidOperationException(
                        $"the centre of slot {i} resolves to {at?.LabelShort ?? "nothing"}, not {entries[i].pawn?.LabelShort ?? "nobody"}");
            }
            var reached = new HashSet<Thing>();
            for (int y = 0; y < 200; y += 3)
            {
                for (int x = 0; x < UI.screenWidth; x += 3)
                {
                    Thing t = bar.ColonistOrCorpseAt(new Vector2(x, y));
                    if (t != null)
                        reached.Add(t);
                }
            }
            foreach (Pawn hidden in r.Vanilla.Where(e => e.pawn != null && !visible.Contains(e.pawn)).Select(e => e.pawn))
            {
                if (reached.Contains(hidden))
                    throw new InvalidOperationException($"the hidden colonist {hidden.LabelShort} can still be hit");
            }
            if (!reached.SetEquals(visible))
                throw new InvalidOperationException(
                    $"reachable [{string.Join(", ", reached.Select(t => t.LabelShort))}] != visible [{string.Join(", ", visible.Select(p => p.LabelShort))}]");
            r.Notes.Add($"{visible.Count} visible, {r.Vanilla.Count(e => e.pawn != null) - visible.Count} hidden, none reachable");
        }

        private static void ClickStep(Scenario s, int slot)
        {
            System.Diagnostics.Process process = null;
            Pawn target = null;
            s.Do($"click the portrait in slot {slot}", () =>
                {
                    ColonistBar bar = Bar;
                    target = BarProbe.CachedEntries(bar)[slot].pawn;
                    Vector2 c = BarProbe.CachedLocs(bar)[slot] + bar.Size / 2f;
                    Find.Selector.ClearSelection();
                    process = Xdo.Start($"mousemove --sync {(int)c.x} {(int)c.y} sleep 0.15 mousedown 1 sleep 0.25 mouseup 1");
                })
                .ExpectWithin($"slot {slot}: xdotool finished", 15, () =>
                {
                    Bar.MarkColonistsDirty();
                    return process.HasExited;
                })
                .Do($"slot {slot}: xdotool succeeded", () => Xdo.Check(process))
                .ExpectWithin($"slot {slot}: its colonist is selected", 10, () =>
                {
                    Bar.MarkColonistsDirty();
                    return Find.Selector.SingleSelectedThing == target;
                }, () => $"{Find.Selector.SelectedObjects.Count} object(s) selected, wanted {target?.LabelShort}");
        }

        // Precondition: 5 colonists. Trigger: the same reorder (an API call, then a real right-button drag) on the vanilla
        // bar as a control, then on the view.
        // Failure mode: a drag in the view reaches ColonistBar.Reorder and rewrites playerSettings.displayOrder (game
        // data); or the check is vacuous because the control never reaches Reorder.
        private static void SeamReorder()
        {
            var s = new Scenario("Seam reorder", 600000);
            Roster r = Prelude(s, 5);
            Dictionary<Pawn, int> orders = null;
            s.Do("remember every display order, restore them when the scenario ends", () =>
            {
                orders = r.Vanilla.Where(e => e.pawn != null).ToDictionary(e => e.pawn, e => e.pawn.playerSettings.displayOrder);
            }).OnFinish(() => RestoreOrders(orders));

            s.Do("control, API: a vanilla entry's reorder action reaches ColonistBar.Reorder and rewrites displayOrder", () =>
                {
                    BarProbe.Start();
                    Bar.Entries[0].reorderAction(0, 2);
                    if (BarProbe.ReorderCalls != 1)
                        throw new InvalidOperationException($"{BarProbe.ReorderCalls} Reorder calls, expected 1");
                    if (!OrdersChanged(orders))
                        throw new InvalidOperationException("the vanilla reorder did not change any display order: the control cannot show anything");
                    RestoreOrders(orders);
                })
                .ExpectWithin("vanilla roster back", 20, () => VanillaBack(r), () => r.Last);

            DragStep(s, "control, real drag on the vanilla bar", 0, 2);
            s.Do("control, real: the right-button drag reached ColonistBar.Reorder", () =>
                {
                    if (BarProbe.ReorderCalls < 1)
                        throw new InvalidOperationException("the real drag never reached ColonistBar.Reorder: input injection is ineffective, so the treatment below proves nothing");
                    r.Notes.Add($"control drag: {BarProbe.ReorderCalls} Reorder call(s)");
                    RestoreOrders(orders);
                })
                .ExpectWithin("vanilla roster back", 20, () => VanillaBack(r), () => r.Last);

            ViewOnSteps(s, r);
            s.Do("API: every entry of the view carries a reorder action and a drag ghost that do nothing", () =>
            {
                BarProbe.Start();
                foreach (ColonistBar.Entry e in BarProbe.CachedEntries(Bar).ToList())
                {
                    e.reorderAction(0, 1);
                    e.reorderAction(1, 0);
                    e.reorderAction(0, 2);
                    e.extraDraggedItemOnGUI(0, Vector2.zero);
                }
                if (BarProbe.ReorderCalls != 0)
                    throw new InvalidOperationException($"{BarProbe.ReorderCalls} Reorder call(s) reached the vanilla writer");
                if (OrdersChanged(orders))
                    throw new InvalidOperationException("a display order changed");
            });
            DragStep(s, "real drag on the view", 0, 2);
            s.Do("the real drag in the view changed nothing", () =>
                {
                    if (BarProbe.ReorderCalls != 0)
                        throw new InvalidOperationException($"{BarProbe.ReorderCalls} Reorder call(s) reached the vanilla writer");
                    if (OrdersChanged(orders))
                        throw new InvalidOperationException("a display order changed");
                })
                .ExpectWithin("the view is still in place", 20, () => ViewInPlace(r), () => r.Last)
                .Start();
        }

        private static void DragStep(Scenario s, string label, int fromSlot, int toSlot)
        {
            System.Diagnostics.Process process = null;
            s.Do($"{label}: right-drag slot {fromSlot} to slot {toSlot}", () =>
                {
                    BarProbe.Start();
                    ColonistBar bar = Bar;
                    List<Vector2> locs = BarProbe.CachedLocs(bar);
                    Vector2 a = locs[fromSlot] + bar.Size / 2f, b = locs[toSlot] + bar.Size / 2f;
                    process = Xdo.Start($"mousemove --sync {(int)a.x} {(int)a.y} sleep 0.2 mousedown 3 sleep 0.3 " +
                        $"mousemove --sync {(int)((a.x + b.x) / 2f)} {(int)a.y} sleep 0.3 mousemove --sync {(int)b.x} {(int)b.y} sleep 0.4 mouseup 3");
                })
                .ExpectWithin($"{label}: xdotool finished", 20, () => process.HasExited)
                .Do($"{label}: xdotool succeeded", () => Xdo.Check(process))
                .WaitFrames(15);
        }

        private static bool OrdersChanged(Dictionary<Pawn, int> before) =>
            before != null && before.Any(kv => kv.Key.playerSettings.displayOrder != kv.Value);

        private static void RestoreOrders(Dictionary<Pawn, int> before)
        {
            if (before == null)
                return;
            foreach (KeyValuePair<Pawn, int> kv in before)
            {
                if (kv.Key.playerSettings != null)
                    kv.Key.playerSettings.displayOrder = kv.Value;
            }
            Bar.MarkColonistsDirty();
        }
    }
}
