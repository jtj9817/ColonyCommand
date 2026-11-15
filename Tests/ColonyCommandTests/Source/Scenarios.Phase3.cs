using System;
using System.Collections.Generic;
using System.Linq;
using ColonyCommand;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace ColonyCommandTests
{
    // Phase 3: the controls. Real keyboard and mouse input through xdotool on the harness's private display (the only way to
    // prove focus, hotkey suppression and Escape: the API cannot press a key), judged by the state the game ends up in.
    // Same cast as Phase 2 (A..F, paused game); see Scenarios.Phase2.cs.
    public static partial class Scenarios
    {
        // ------------------------------------------------------------------------------------------- input steps

        private static Vector2 Center(Rect r) => new Vector2(r.x + r.width / 2f, r.y + r.height / 2f);

        private static string ClickArgs(Vector2 p) => $"mousemove --sync {(int)p.x} {(int)p.y} sleep 0.2 mousedown 1 sleep 0.3 mouseup 1";

        /// <summary>Sends xdotool input (computed when the step runs, from where the controls are then) and waits for the game to take it in.</summary>
        private static void InputStep(Scenario s, string label, Func<string> args)
        {
            System.Diagnostics.Process process = null;
            s.Do($"{label}: send the input", () =>
                {
                    Messages.Clear();   // a message is a window over the bar and swallows real input beneath it
                    InputProbe.Clear();
                    string a = args();
                    Placement p = TheWindow().CurrentPlacement;
                    CCT.Info($"input '{label}': xdotool {a}; scale {BarProbe.CachedScale(Bar):F2}, {BarProbe.CachedEntries(Bar).Count} entries, dirty {BarProbe.IsDirty(Bar)}; strip {p.Strip}, search {p.Search}, icon {p.SearchIcon}, chip1 {TheWindow().ChipRect(1)}");
                    process = Xdo.Start(a);
                })
                .ExpectWithin($"{label}: xdotool finished", 20, () => process.HasExited)
                .Do($"{label}: xdotool succeeded", () => Xdo.Check(process))
                .WaitFrames(8)
                .Do($"{label}: what the game received", () => CCT.Info($"input '{label}' arrived as: {InputProbe.Describe()}"));
        }

        private static void InputStep(Scenario s, string label, string args) => InputStep(s, label, () => args);

        private static void ClickStepAt(Scenario s, string label, Func<Vector2> where) =>
            InputStep(s, label, () => ClickArgs(where()));

        /// <summary>
        /// A real click on the search field, or on its icon when the layout is compact (the field opens and takes the focus),
        /// then waits for the keyboard focus.
        /// </summary>
        private static void FocusSearch(Scenario s)
        {
            ClickStepAt(s, "click the search field (or its icon in the compact layout)", () =>
            {
                Placement p = TheWindow().CurrentPlacement;
                return Center(p.SearchFieldShown ? p.Search : p.SearchIcon);
            });
            s.ExpectWithin("the field holds the keyboard focus", 10, () => TheWindow().SearchFocused,
                () => $"placement: field shown {TheWindow().CurrentPlacement.SearchFieldShown}, compact {TheWindow().CurrentPlacement.SearchCompact}, field {TheWindow().CurrentPlacement.Search}, icon {TheWindow().CurrentPlacement.SearchIcon}; mouse {UI.MousePositionOnUIInverted}; input seen: {InputProbe.Describe()}; {InputDiagnostics()}");
        }

        private static Vector2 ClearButton(ColonyCommandWindow w)
        {
            Rect field = w.CurrentPlacement.Search;
            return new Vector2(field.xMax - 13f, field.y + 12f);   // the widget's x sits 4 px inside the end of its text box
        }

        private static bool Paused => Find.TickManager.CurTimeSpeed == TimeSpeed.Paused;

        // ------------------------------------------------------------------------------------------ Search field

        // Precondition: the cast staged and paused, no criteria. Trigger: real clicks and keys. A click on the field, the
        // word "quill", the space bar, Escape; the controls with the field unfocused as controls; then a status and ×.
        // Failure modes: the bar waits for Enter instead of following the typing (FR-02); the field does not take the keyboard
        // focus, or the game's hotkeys (pause) fire while a letter is typed; Escape opens the main menu instead of only
        // dismissing the focus (FR-05); × clears more than the query. Controls: the pause key works unfocused, and Escape
        // unfocused really opens the main menu, so the two suppressions above are not vacuous.
        private static void SearchField()
        {
            var s = new Scenario("Search field", 4000000);
            var r = new Roster();
            Cast c = Stage(s, r);
            s.OnFinish(() =>
            {
                if (Find.MainTabsRoot.OpenTab != null)
                    Find.MainTabsRoot.EscapeCurrentTab(false);
            });

            InputStep(s, "EXPERIMENT sacrificial click on the map", "mousemove --sync 700 300 sleep 0.3 mousedown 1 sleep 0.3 mouseup 1");
            FocusSearch(s);
            InputStep(s, "type 'quill'", "type --delay 120 quill");
            s.ExpectWithin("typing alone, no Enter, narrowed the bar to A", 20,
                () => ViewState.Query == "quill" && Shows(r, c, Letters(c, "A")),
                () => $"query '{ViewState.Query}', field '{TheWindow().Search.filter.Text}', focused {TheWindow().SearchFocused}; {r.Last}; input seen: {InputProbe.Describe()}");
            InputStep(s, "press the space bar while the field is focused", "key space");
            s.Do("the space went to the field and the game stayed paused: the pause hotkey was suppressed", () =>
            {
                if (!Paused)
                    throw new InvalidOperationException($"the game is running ({Find.TickManager.CurTimeSpeed}): the pause hotkey fired while typing");
                if (ViewState.Query != "quill ")
                    throw new InvalidOperationException($"the query is '{ViewState.Query}', expected 'quill ' (the space was typed)");
            });
            InputStep(s, "press Escape while the field is focused", "key Escape");
            s.Do("Escape only dismissed the focus: the query and the view are kept, the main menu did not open", () =>
                {
                    if (TheWindow().SearchFocused)
                        throw new InvalidOperationException("the field is still focused");
                    if (ViewState.Query != "quill ")
                        throw new InvalidOperationException($"the query changed to '{ViewState.Query}'");
                    if (Find.MainTabsRoot.OpenTab != null)
                        throw new InvalidOperationException($"Escape opened the main tab {Find.MainTabsRoot.OpenTab.defName}");
                })
                .ExpectWithin("still showing A", 10, () => Shows(r, c, Letters(c, "A")), () => r.Last);

            InputStep(s, "control: space bar with the field unfocused", "key space");
            s.ExpectWithin("control: the pause hotkey works again, so the suppression above was real", 10, () => !Paused,
                    () => $"still {Find.TickManager.CurTimeSpeed}")
                .Do("pause again", () => Find.TickManager.CurTimeSpeed = TimeSpeed.Paused);
            InputStep(s, "control: Escape with the field unfocused", "key Escape");
            s.ExpectWithin("control: it opens the main menu, so the Escape that only unfocused was consumed by the field", 10,
                    () => Find.MainTabsRoot.OpenTab == MainButtonDefOf.Menu, () => $"open tab {Find.MainTabsRoot.OpenTab?.defName ?? "none"}")
                .Do("close the menu", () => Find.MainTabsRoot.EscapeCurrentTab(false));

            ViewStep(s, r, c, "a status (Drafted) is set as well", () => ViewState.Status = StatusFilter.Drafted, Is(""));
            ClickStepAt(s, "click the field's ×", () => ClearButton(TheWindow()));
            s.ExpectWithin("× cleared the query and nothing else: the status stays, B (drafted) shows", 20,
                    () => ViewState.Query == "" && ViewState.Status == StatusFilter.Drafted && Shows(r, c, Letters(c, "B")),
                    () => $"query '{ViewState.Query}', status {ViewState.Status}; {r.Last}")
                .Do("the field is empty too", () =>
                {
                    if (TheWindow().Search.filter.Text != "")
                        throw new InvalidOperationException($"the field still holds '{TheWindow().Search.filter.Text}'");
                })
                .Start();
        }

        // ------------------------------------------------------------------------------------------- Search Enter

        // Precondition: the cast staged and paused, B selected. Trigger: real Enter in the field.
        // Failure modes: Enter selects the wrong colonist or does not replace the selection like a click does; a dead
        // colonist's corpse is not what gets selected; Enter acts with a whitespace-only query or with the field unfocused.
        private static void SearchEnter()
        {
            var s = new Scenario("Search Enter", 4000000);
            var r = new Roster();
            Cast c = Stage(s, r);
            s.Do("select B", () =>
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(c.B, playSound: false);
            });
            FocusSearch(s);
            InputStep(s, "type 'quill'", "type --delay 120 quill");
            s.ExpectWithin("A is the only result", 20, () => Shows(r, c, Letters(c, "A")), () => r.Last)
                .Do("typing selected nothing: B is still the selection", () =>
                {
                    if (Find.Selector.SingleSelectedThing != c.B)
                        throw new InvalidOperationException("the selection changed while typing");
                });
            InputStep(s, "press Enter", "key Return");
            s.ExpectWithin("A, the first result, is selected as a click on its portrait would select it", 10,
                    () => Find.Selector.SingleSelectedThing == c.A,
                    () => $"selected: {string.Join(", ", Find.Selector.SelectedObjects.Select(o => o.ToString()))}")
                .Do("the field kept its text and its focus: Enter was consumed, not typed", () =>
                {
                    if (ViewState.Query != "quill" || !TheWindow().SearchFocused)
                        throw new InvalidOperationException($"query '{ViewState.Query}', focused {TheWindow().SearchFocused}");
                });

            // The first result of two: the first in the bar's order, not the best match.
            s.Do("select B again, then narrow to A and D", () =>
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(c.B, playSound: false);
                ViewState.Query = "qui";
            }).ExpectWithin("A and D are shown", 20, () => Shows(r, c, Letters(c, "AD")), () => r.Last);
            InputStep(s, "press Enter", "key Return");
            s.ExpectWithin("the first of them in bar order is selected", 10,
                () => Find.Selector.SingleSelectedThing == c.R.Vanilla.Select(e => e.pawn).First(p => p == c.A || p == c.D));

            s.Do("a dead colonist is the only result", () => ViewState.Query = "ghost")
                .ExpectWithin("F is shown", 20, () => Shows(r, c, Letters(c, "F")), () => r.Last);
            InputStep(s, "press Enter", "key Return");
            s.ExpectWithin("F's corpse is selected (what a click on a dead colonist's portrait selects)", 10,
                () => Find.Selector.SingleSelectedThing == c.F.Corpse,
                () => $"selected: {string.Join(", ", Find.Selector.SelectedObjects.Select(o => o.ToString()))}");

            // Gates: select B and press Enter in two states where it must do nothing.
            s.Do("select B; a whitespace-only query", () =>
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(c.B, playSound: false);
                ViewState.Query = "   ";
            }).ExpectWithin("the bar is vanilla (whitespace is no criterion)", 20, () => VanillaBack(r), () => r.Last);
            InputStep(s, "press Enter with a whitespace-only query", "key Return");
            s.Do("nothing happened: B is still selected", () =>
            {
                if (Find.Selector.SingleSelectedThing != c.B)
                    throw new InvalidOperationException("Enter selected something although the query holds only whitespace");
            });
            s.Do("unfocus, then a real query", () =>
                {
                    TheWindow().Search.Unfocus();
                    ViewState.Query = "quill";
                })
                .ExpectWithin("A is shown and the field is not focused", 20, () => Shows(r, c, Letters(c, "A")) && !TheWindow().SearchFocused, () => r.Last);
            InputStep(s, "press Enter with the field unfocused", "key Return");
            s.Do("nothing happened: B is still selected", () =>
                {
                    if (Find.Selector.SingleSelectedThing != c.B)
                        throw new InvalidOperationException("Enter selected something although the field is not focused");
                })
                .Start();
        }

        // ------------------------------------------------------------------------------------------- Chip clicks

        // Precondition: the cast staged and paused, the name token set (isolating the cast). Trigger: real clicks on the chips.
        // Failure modes: a chip click sets the wrong status, a second click does not toggle it off, a second chip is
        // added instead of replacing the first, All drops the search or leaves status/skill set, a chip label's count is
        // stale (spec 3.2: counts follow the game).
        private static void ChipClicks()
        {
            var s = new Scenario("Chip clicks", 4000000);
            var r = new Roster();
            Cast c = Stage(s, r);
            ViewStep(s, r, c, "the token isolates the cast", () => ViewState.Query = Token, Letters(c, "ABCDEF"));
            s.ExpectWithin("every chip label carries its live count", 10, () => ChipLabelsMatch(), () => ChipLabelsText());
            int clicks = 0;
            void Chip(string name, int index, StatusFilter expectStatus, string expectLetters)
            {
                ClickStepAt(s, $"click {name} ({++clicks})", () => Center(TheWindow().ChipRect(index)));
                s.ExpectWithin($"{name}: status {expectStatus}, the bar shows [{expectLetters}]", 20,
                    () => ViewState.Status == expectStatus && Shows(r, c, Letters(c, expectLetters)),
                    () => $"status {ViewState.Status}; {r.Last}; chip {TheWindow().ChipRect(index)}, mouse {UI.MousePositionOnUIInverted}; {InputDiagnostics()}");
            }
            Chip("Drafted", 1, StatusFilter.Drafted, "B");
            Chip("Drafted again: toggled off", 1, StatusFilter.None, "ABCDEF");
            Chip("Injured", 2, StatusFilter.Injured, "AD");
            Chip("Idle: replaces Injured, no union", 3, StatusFilter.Idle, "AC");
            s.Do("a skill criterion as well (a click on All must clear it too)", () =>
                {
                    ViewState.Skill = c.Skill;
                    ViewState.MinLevel = 12;
                })
                .ExpectWithin("Idle and skill >= 12: A and C", 20, () => Shows(r, c, Letters(c, "AC")), () => r.Last);
            ClickStepAt(s, "click All", () => Center(TheWindow().ChipRect(0)));
            s.ExpectWithin("All: status and skill criterion gone, the search stays", 20,
                    () => ViewState.Status == StatusFilter.None && ViewState.MinLevel == ViewState.AnyLevel && ViewState.Query == Token
                        && Shows(r, c, Letters(c, "ABCDEF")), () => $"status {ViewState.Status}, min {ViewState.MinLevel}, query '{ViewState.Query}'; {r.Last}")
                .Do("the labels still match the counts", () =>
                {
                    if (!ChipLabelsMatch())
                        throw new InvalidOperationException(ChipLabelsText());
                })
                .Start();
        }

        private static bool ChipLabelsMatch()
        {
            ColonyCommandWindow w = TheWindow();
            RosterCounts k = BarAdapter.Counts;
            int[] want = { k.All, k.Drafted, k.Injured, k.Idle };
            for (int i = 0; i < 4; i++)
            {
                if (!w.ChipLabel(i).EndsWith(" " + want[i]))
                    return false;
            }
            return true;
        }

        private static string ChipLabelsText()
        {
            ColonyCommandWindow w = TheWindow();
            RosterCounts k = BarAdapter.Counts;
            return $"labels [{w.ChipLabel(0)} | {w.ChipLabel(1)} | {w.ChipLabel(2)} | {w.ChipLabel(3)}], counts {k.All}/{k.Drafted}/{k.Injured}/{k.Idle}";
        }

        // ------------------------------------------------------------------------------------------ Empty state

        // Precondition: the cast staged and paused. Trigger: a query nobody matches, typed for real; a status that nobody
        // satisfies; then a real click on Reset.
        // Failure modes: no "No matching colonists" line (FR-04), Reset unreachable (off screen, covered) or not clearing the
        // search field (state and field out of step), the empty bar left broken.
        private static void EmptyState()
        {
            var s = new Scenario("Empty state", 4000000);
            var r = new Roster();
            Cast c = Stage(s, r);
            s.Do("with results the line is not shown", () => ViewState.Query = Token)
                .ExpectWithin("the cast is shown", 20, () => Shows(r, c, Letters(c, "ABCDEF")), () => r.Last)
                .Do("no empty state", () =>
                {
                    if (TheWindow().CurrentPlacement.ShowEmpty)
                        throw new InvalidOperationException("the empty-state line shows although there are results");
                })
                .Do("clear the query", () => ViewState.Query = "")
                .ExpectWithin("vanilla", 20, () => VanillaBack(r), () => r.Last);

            FocusSearch(s);
            InputStep(s, "type a query nobody matches", "type --delay 120 zzqx");
            s.ExpectWithin("the bar is empty and the empty-state line shows", 20,
                    () => ViewState.Query == "zzqx" && !BarProbe.IsDirty(Bar) && BarProbe.CachedEntries(Bar).Count == 0 && TheWindow().CurrentPlacement.ShowEmpty,
                    () => $"query '{ViewState.Query}', entries {BarProbe.CachedEntries(Bar).Count}, empty line {TheWindow().CurrentPlacement.ShowEmpty}")
                .Do("the line and Reset are on screen, the field is still there, the chips are below", () =>
                {
                    Placement p = TheWindow().CurrentPlacement;
                    var screen = new Rect(0f, 0f, UI.screenWidth, UI.screenHeight);
                    foreach ((string name, Rect rect) in new[] { ("empty line", p.Empty), ("Reset", TheWindow().ResetRect), ("search", p.Search), ("chips", p.Strip) })
                    {
                        if (rect.width <= 0f || rect.xMin < 0f || rect.yMin < 0f || rect.xMax > screen.xMax || rect.yMax > screen.yMax)
                            throw new InvalidOperationException($"{name} {rect} is not on the {screen} screen");
                    }
                    if (p.Strip.yMin < p.Empty.yMax)
                        throw new InvalidOperationException($"the chips {p.Strip} overlap the empty-state line {p.Empty}");
                    if (TheWindow().ResetRect.xMax > p.Empty.xMax + 0.5f || TheWindow().ResetRect.xMin < p.Empty.xMin)
                        throw new InvalidOperationException($"Reset {TheWindow().ResetRect} is outside its line {p.Empty}");
                });
            ClickStepAt(s, "click Reset", () => Center(TheWindow().ResetRect));
            s.ExpectWithin("Reset: the state is at its defaults, the field is empty, the bar is vanilla again", 20, () =>
                    !ViewState.AnythingSet && TheWindow().Search.filter.Text == "" && !TheWindow().CurrentPlacement.ShowEmpty && VanillaBack(r),
                    () => $"anything set {ViewState.AnythingSet}, field '{TheWindow().Search.filter.Text}', empty line {TheWindow().CurrentPlacement.ShowEmpty}; {r.Last}");

            // A status nobody satisfies is an empty result too.
            s.Do("a name that matches only a dead colonist, with the Drafted status (a corpse is never drafted)", () =>
                {
                    ViewState.Query = "ghost";
                    ViewState.Status = StatusFilter.Drafted;
                })
                .ExpectWithin("the bar is empty and the line shows", 20,
                    () => !BarProbe.IsDirty(Bar) && BarProbe.CachedEntries(Bar).Count == 0 && TheWindow().CurrentPlacement.ShowEmpty,
                    () => $"entries {BarProbe.CachedEntries(Bar).Count}, line {TheWindow().CurrentPlacement.ShowEmpty}");
            ClickStepAt(s, "click Reset", () => Center(TheWindow().ResetRect));
            s.ExpectWithin("everything cleared", 20, () => !ViewState.AnythingSet && TheWindow().Search.filter.Text == "" && VanillaBack(r), () => r.Last)
                .Start();
        }
    }
}
