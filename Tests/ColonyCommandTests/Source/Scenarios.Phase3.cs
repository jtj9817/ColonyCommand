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
        /// repeated until the field holds the keyboard focus. The retry is an observed property of the harness: now and then
        /// the first click of a run reaches the game's OnGUI root but never the window stack (no exception, not reproducible
        /// by hand); the attempts needed are written to the run summary so the rate stays visible.
        /// </summary>
        private static void FocusSearch(Scenario s, Roster r)
        {
            System.Diagnostics.Process process = null;
            int attempts = 0;
            float sentAt = 0f;
            s.Expect("click the search field (its icon in the compact layout) until it holds the keyboard focus", () =>
                {
                    ColonyCommandWindow w = TheWindow();
                    if (w.SearchFocused)
                    {
                        r.Notes.Add($"focus click: {attempts} attempt(s)");
                        return true;
                    }
                    if (process != null && !process.HasExited)
                        return false;
                    if (process != null)
                    {
                        Xdo.Check(process);
                        process = null;
                    }
                    if (attempts > 0 && Time.realtimeSinceStartup - sentAt < 2.5f)
                        return false;   // the game gets time to react before the next try
                    if (attempts >= 4)
                        throw new TimeoutException($"{attempts} clicks, the field never took the focus; input seen by the game: {InputProbe.Describe()}; {InputDiagnostics()}");
                    Placement p = w.CurrentPlacement;
                    Messages.Clear();   // a message is a window over the bar and swallows real input beneath it
                    InputProbe.Clear();
                    process = Xdo.Start(ClickArgs(Center(p.SearchFieldShown ? p.Search : p.SearchIcon)));
                    sentAt = Time.realtimeSinceStartup;
                    attempts++;
                    return false;
                })
                .WaitFrames(8);
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

            FocusSearch(s, r);
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
            s.Do("count main menu requests from here", () => MenuRequests.Count = 0);
            InputStep(s, "press Escape while the field is focused", "key Escape");
            s.Do("Escape only dismissed the focus: the query and the view are kept, the main menu was not requested", () =>
                {
                    if (TheWindow().SearchFocused)
                        throw new InvalidOperationException("the field is still focused");
                    if (ViewState.Query != "quill ")
                        throw new InvalidOperationException($"the query changed to '{ViewState.Query}'");
                    if (MenuRequests.Count != 0)
                        throw new InvalidOperationException($"Escape asked for the main menu {MenuRequests.Count} time(s)");
                })
                .ExpectWithin("still showing A", 10, () => Shows(r, c, Letters(c, "A")), () => r.Last);

            InputStep(s, "control: space bar with the field unfocused", "key space");
            s.ExpectWithin("control: the pause hotkey works again, so the suppression above was real", 10, () => !Paused,
                    () => $"still {Find.TickManager.CurTimeSpeed}")
                .Do("pause again; the few ticks that ran let vanilla's AutoUndrafter undraft B (the tick counter was moved two days), so draft B again", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    if (!c.B.Drafted)
                        Fixtures.Draft(c.B);
                    Fixtures.SetJobTag(c.B, Verse.AI.JobTag.DraftedOrder);
                });
            InputStep(s, "control: Escape with the field unfocused", "key Escape");
            s.Do("control: Escape asked for the main menu, so the Escape above that did not was consumed by the field", () =>
                {
                    if (MenuRequests.Count < 1)
                        throw new InvalidOperationException("an unfocused Escape did not ask for the main menu: the check above proves nothing");
                    r.Notes.Add($"control Escape: {MenuRequests.Count} menu request(s)");
                })
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
            FocusSearch(s, r);
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
            // The first shown colonist, read from the bar's own list (a random map colonist whose name contains "qui" may come first).
            Pawn firstShown = null;
            s.Do("the first colonist the bar shows", () => firstShown = ShownPawns().First())
                .ExpectWithin("it is selected, as a click on its portrait would select it", 10,
                    () => Find.Selector.SingleSelectedThing == firstShown,
                    () => $"first shown {firstShown?.LabelShort}; selected [{string.Join(", ", Find.Selector.SelectedObjects.Select(o => o.ToString()))}]");

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
            InputStep(s, "press Escape to unfocus the field", "key Escape");
            s.Do("a real query", () => ViewState.Query = "quill")
                .ExpectWithin("A is shown and the field is not focused", 20, () => Shows(r, c, Letters(c, "A")) && !TheWindow().SearchFocused,
                    () => $"focused {TheWindow().SearchFocused}; {r.Last}");
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
            float stripY = -1f;
            s.WaitFrames(8).Do("remember where the chip row is", () => stripY = TheWindow().ChipRect(1).y);
            ViewStep(s, r, c, "the token isolates the cast", () => ViewState.Query = Token, Letters(c, "ABCDEF"));
            s.Do("the chip row did not move when the bar lost three colonists", () => AssertChipRowAt(stripY));
            s.ExpectWithin("every chip label carries its live count", 10, () => ChipLabelsMatch(), () => ChipLabelsText());
            int clicks = 0;
            void Chip(string name, int index, StatusFilter expectStatus, string expectLetters)
            {
                ClickStepAt(s, $"click {name} ({++clicks})", () => Center(TheWindow().ChipRect(index)));
                s.ExpectWithin($"{name}: status {expectStatus}, the bar shows [{expectLetters}]", 20,
                    () => ViewState.Status == expectStatus && Shows(r, c, Letters(c, expectLetters)),
                    () => $"status {ViewState.Status}; {r.Last}; chip {TheWindow().ChipRect(index)}, mouse {UI.MousePositionOnUIInverted}; {InputDiagnostics()}");
                s.Do($"{name}: the chips stayed where they were (a second click lands on the same chip)", () => AssertChipRowAt(stripY));
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

        private static void AssertChipRowAt(float y)
        {
            float now = TheWindow().ChipRect(1).y;
            if (Mathf.Abs(now - y) > 0.5f)
                throw new InvalidOperationException($"the chip row moved from y={y:F1} to y={now:F1} while filtering: a second click would miss the chip");
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
            float stripY = -1f;
            s.WaitFrames(8).Do("remember where the chip row is", () => stripY = TheWindow().ChipRect(1).y);
            s.Do("with results the line is not shown", () => ViewState.Query = Token)
                .ExpectWithin("the cast is shown", 20, () => Shows(r, c, Letters(c, "ABCDEF")), () => r.Last)
                .Do("no empty state", () =>
                {
                    if (TheWindow().CurrentPlacement.ShowEmpty)
                        throw new InvalidOperationException("the empty-state line shows although there are results");
                })
                .Do("clear the query", () => ViewState.Query = "")
                .ExpectWithin("vanilla", 20, () => VanillaBack(r), () => r.Last);

            FocusSearch(s, r);
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
                    AssertChipRowAt(stripY);   // an empty result must not move the chips either
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

        // ------------------------------------------------------------------------------------- Enter across maps

        // Precondition: 6 colonists on the home map, a second home map with two colonists, a caravan of two. Trigger: real Enter
        // with the query naming one colonist of each place, then (in the world view) one on the home map.
        // Failure modes: Enter selects nothing or the wrong colonist for a colonist on another map (the map must follow), a
        // caravan member (vanilla selects the caravan and shows the world), or in the world view (it must jump back to the map).
        private static void EnterAcrossMaps()
        {
            var s = new Scenario("Enter across maps", 4000000);
            Roster r = Prelude(s, 6);
            Map home = null, second = null;
            Settlement settlement = null;
            Caravan caravan = null;
            var members = new List<Pawn>();
            Pawn Named(string nick) => members.First(p => ((NameTriple)p.Name).Nick == nick);
            s.OnFinish(() =>
            {
                if (WorldRendererUtility.WorldSelected)
                    CameraJumper.TryHideWorld();
                if (home != null && !home.Disposed && Current.Game.CurrentMap != home)
                    Current.Game.CurrentMap = home;
                Find.WorldSelector.ClearSelection();
                if (caravan != null && !caravan.Destroyed)
                    Find.WorldObjects.Remove(caravan);
                foreach (Pawn p in members.Where(p => !p.Destroyed))
                    p.Destroy();
                if (settlement != null && !settlement.Destroyed)
                    Find.WorldObjects.Remove(settlement);
            });
            s.Do("settle a second home map with two colonists and form a caravan of two on the home tile", () =>
            {
                home = Find.CurrentMap;
                PlanetTile tile = TileFinder.RandomSettlementTileFor(Faction.OfPlayer, mustBeAutoChoosable: false,
                    extraValidator: t => !Find.WorldObjects.AnyWorldObjectAt(t));
                settlement = SettleUtility.AddNewHome(tile, Faction.OfPlayer);
                second = GetOrGenerateMapUtility.GetOrGenerateMap(tile, new IntVec3(60, 1, 60), null);
                // Pawn generation comes first and all names are triples: a generated pawn may pick any existing one as a parent.
                for (int i = 0; i < 2; i++)
                {
                    Pawn p = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                    p.Name = new NameTriple("Probe", $"Second{i}", "Fixture");
                    GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(second.Center, second, 8), second);
                    members.Add(p);
                }
                var caravanPawns = new List<Pawn>();
                for (int i = 0; i < 2; i++)
                {
                    Pawn p = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                    p.Name = new NameTriple("Probe", $"Caravan{i}", "Fixture");
                    members.Add(p);
                    caravanPawns.Add(p);
                }
                caravan = CaravanMaker.MakeCaravan(caravanPawns, Faction.OfPlayer, home.Tile, addToWorldPawnsIfNotAlready: true);
                Messages.Clear();
            });
            CaptureSteps(s, r, 10);   // 6 home + 2 second map + 2 caravan
            s.Do("the home map is current", () =>
            {
                if (Find.CurrentMap != home)
                    Current.Game.CurrentMap = home;
            });
            FocusSearch(s, r);

            // 1. a colonist on the second map
            InputStep(s, "type 'second0'", "type --delay 120 second0");
            s.ExpectWithin("the second map's colonist is the only result", 20, () => ShownOnly(Named("Second0")),
                () => $"query '{ViewState.Query}', field '{TheWindow().Search.filter.Text}', counts all {BarAdapter.Counts.All} of {BarAdapter.Counts.Eligible}, " +
                    $"applied {BarAdapter.Applied} for version {BarAdapter.AppliedVersion} (now {ViewState.Version}), shown count {BarAdapter.Shown}, dirty {BarProbe.IsDirty(Bar)}, " +
                    $"cached entries [{BarProbe.Describe(BarProbe.CachedEntries(Bar))}], locs {BarProbe.CachedLocs(Bar).Count}, second0 '{Named("Second0").LabelShort}' on map {Named("Second0").Map?.uniqueID.ToString() ?? "none"}, dead {Named("Second0").Dead}");
            InputStep(s, "press Enter", "key Return");
            s.ExpectWithin("selected, and the current map followed to the second map", 15,
                () => Find.Selector.SingleSelectedThing == Named("Second0") && Find.CurrentMap == second,
                () => $"selected [{string.Join(", ", Find.Selector.SelectedObjects.Select(o => o.ToString()))}], current map is {(Find.CurrentMap == second ? "the second" : "the home")} map");

            // 2. a caravan member: vanilla selects the caravan and shows the world. (A jump to another map takes the keyboard
            //    focus out of the field, as it does for any vanilla UI: focus it again before typing.)
            s.Do("the query becomes 'caravan0' (typing is proven by the scenarios above; the Enter below is real)", () => ViewState.Query = "caravan0");
            s.ExpectWithin("the caravan member is the only result", 20, () => ShownOnly(Named("Caravan0")),
                () => $"query '{ViewState.Query}', field '{TheWindow().Search.filter.Text}', shown [{Names(ShownPawns())}]");
            FocusSearch(s, r);
            InputStep(s, "press Enter", "key Return");
            s.ExpectWithin("the caravan is selected in the world view", 15,
                () => WorldRendererUtility.WorldSelected && Find.WorldSelector.IsSelected(caravan),
                () => $"world view {WorldRendererUtility.WorldSelected}, selected world objects {Find.WorldSelector.NumSelectedObjects}");

            // 3. in the world view, a colonist on a map: vanilla jumps to the map and selects it
            s.Do("the query becomes 'probe3' (a home colonist)", () => ViewState.Query = "probe3");
            s.ExpectWithin("a home colonist is the only result", 20,
                () => ShownPawns().Count == 1 && ((NameTriple)ShownPawns()[0].Name).Nick == "Probe3", () => $"shown [{Names(ShownPawns())}]");
            FocusSearch(s, r);
            InputStep(s, "press Enter in the world view", "key Return");
            s.ExpectWithin("the map is showing again with that colonist selected", 15,
                () => !WorldRendererUtility.WorldSelected && Find.Selector.SingleSelectedThing is Pawn p && ((NameTriple)p.Name).Nick == "Probe3",
                () => $"world view {WorldRendererUtility.WorldSelected}, selected [{string.Join(", ", Find.Selector.SelectedObjects.Select(o => o.ToString()))}]")
                .Start();
        }

        private static bool ShownOnly(Pawn pawn)
        {
            List<Pawn> shown = ShownPawns();
            return shown.Count == 1 && shown[0] == pawn && !BarProbe.IsDirty(Bar);
        }

        // ---------------------------------------------------------------------------------------- Controls placement

        // Precondition: the map's own 3 colonists, then topped up to 6, 12 and 24 (1 to 3 rows, shrinking portraits), a caravan
        // (a second group, frames), a view, the bar switched off. Trigger: where the controls are, from the real bar's lists;
        // and the pure layout over screen sizes, bar shapes and a text too wide for the screen.
        // Failure modes: a control overlapping a portrait, its name label, a group frame, the alert column at the right edge,
        // each other or the edge of the screen; the controls staying on screen with the bar hidden; no collapse when the chips
        // do not fit.
        private static void ControlsPlacement()
        {
            var s = new Scenario("Controls placement", 4000000);
            Roster r = Prelude(s, 3);
            Caravan caravan = null;
            var members = new List<Pawn>();
            s.OnFinish(() =>
            {
                Find.PlaySettings.showColonistBar = true;
                if (caravan != null && !caravan.Destroyed)
                    Find.WorldObjects.Remove(caravan);
                foreach (Pawn p in members.Where(p => !p.Destroyed))
                    p.Destroy();
            });
            s.Do("the pure layout over screen sizes and bar shapes", () => PureLayoutSweep(r));
            foreach (int n in new[] { 3, 6, 12, 24 })
            {
                int total = n;
                s.Do($"{total} colonists", () => Fixtures.EnsureColonists(s, total))
                    .ExpectWithin($"{total} colonists captured", 30, () => CaptureVanilla(r, total), () => r.Last)
                    .WaitFrames(8)
                    .Do($"{total} colonists: the controls are clear of the portraits", () => AssertControlsClear(r, $"{total} colonists, vanilla"))
                    .Do($"{total} colonists: a view that keeps only the fixtures", () => ViewState.Query = "Fixture")
                    .WaitFrames(8)
                    .Do($"{total} colonists with a view: the controls are still clear", () => AssertControlsClear(r, $"{total} colonists, view"))
                    .Do("no criteria", () => ViewState.Reset())
                    .ExpectWithin("vanilla", 20, () => VanillaBack(r), () => r.Last);
            }
            s.Do("a caravan of two: a second group with frames", () =>
                {
                    for (int i = 0; i < 2; i++)
                    {
                        Pawn p = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                        p.Name = new NameTriple("Probe", $"Caravan{i}", "Fixture");
                        members.Add(p);
                    }
                    caravan = CaravanMaker.MakeCaravan(members, Faction.OfPlayer, Find.CurrentMap.Tile, addToWorldPawnsIfNotAlready: true);
                })
                .ExpectWithin("26 colonists", 30, () => CaptureVanilla(r, 26), () => r.Last)
                .WaitFrames(8)
                .Do("two groups: the controls are clear of the portraits and the group frames", () => AssertControlsClear(r, "two groups"))
                .Do("hide the colonist bar (a vanilla setting)", () => Find.PlaySettings.showColonistBar = false)
                .WaitFrames(8)
                .Do("the controls went with it: nothing placed, nothing focused, the game's hotkeys are free", () =>
                {
                    if (TheWindow().CurrentPlacement.Visible)
                        throw new InvalidOperationException("the controls show although the colonist bar is hidden");
                    if (TheWindow().CommonSearchWidget != null)
                        throw new InvalidOperationException("the hidden field is still registered as a search widget");
                    Find.PlaySettings.showColonistBar = true;
                })
                .Start();
        }

        private static void AssertControlsClear(Roster r, string label)
        {
            Placement p = TheWindow().CurrentPlacement;
            if (!p.Visible)
                throw new InvalidOperationException($"{label}: the controls are not placed");
            ColonistBar bar = Bar;
            float scale = bar.Scale;
            Vector2 size = bar.Size;
            List<ColonistBar.Entry> entries = BarProbe.CachedEntries(bar);
            int groups = entries.Count == 0 ? 0 : entries[entries.Count - 1].group + 1;
            float margin = groups >= 2 ? 12f * scale : 0f;
            // What the bar draws: each portrait, the group frame around it (12 * scale), and under it the name label (about 20 px of
            // text), which hangs out of the frame, so below the portrait the larger of the two counts.
            var drawn = new List<Rect>();
            foreach (Vector2 loc in BarProbe.CachedLocs(bar))
                drawn.Add(new Rect(loc.x - margin, loc.y - margin, size.x + 2f * margin, margin + size.y + Mathf.Max(Mathf.Max(20f, 32f * scale), margin)));
            var screen = new Rect(0f, 0f, UI.screenWidth, UI.screenHeight);
            var controls = new List<(string name, Rect rect)> { ("chips", p.Strip) };
            if (p.SearchFieldShown)
                controls.Add(("search field", p.Search));
            if (p.SearchCompact)
                controls.Add(("search icon", p.SearchIcon));
            if (p.ShowEmpty)
                controls.Add(("empty line", p.Empty));
            foreach ((string name, Rect rect) in controls)
            {
                if (rect.width <= 0f || rect.xMin < 0f || rect.yMin < 0f || rect.xMax > screen.xMax || rect.yMax > screen.yMax)
                    throw new InvalidOperationException($"{label}: the {name} {rect} is not on the {screen.size} screen");
                if (rect.xMax > UI.screenWidth - ControlsLayout.AlertsWidth)
                    throw new InvalidOperationException($"{label}: the {name} {rect} reaches into the alert column (x > {UI.screenWidth - ControlsLayout.AlertsWidth})");
                foreach (Rect portrait in drawn)
                {
                    if (rect.Overlaps(portrait))
                        throw new InvalidOperationException($"{label}: the {name} {rect} overlaps the portrait area {portrait}");
                }
            }
            for (int i = 0; i < controls.Count; i++)
            {
                for (int j = i + 1; j < controls.Count; j++)
                {
                    if (controls[i].rect.Overlaps(controls[j].rect))
                        throw new InvalidOperationException($"{label}: the {controls[i].name} {controls[i].rect} overlaps the {controls[j].name} {controls[j].rect}");
                }
            }
            r.Notes.Add($"{label}: scale {scale:F2}, {groups} group(s), strip y {p.Strip.y:F0}, {(p.SearchCompact ? "search icon" : "search field")}");
        }

        /// <summary>ControlsLayout.Compute alone: screens from the 800x500 minimum to ultrawide, bars from one portrait to the widest vanilla allows.</summary>
        private static void PureLayoutSweep(Roster r)
        {
            int cases = 0, compactSearch = 0, compactStrip = 0;
            foreach (int w in new[] { 800, 1024, 1280, 1600, 1920, 2560, 3440 })
            {
                foreach (int h in new[] { 500, 720, 1080, 1440 })
                {
                    float maxHalf = (w - 520f) / 2f;   // the vanilla bar: screenWidth - 520 wide, centred
                    foreach (float half in new[] { 14f, 96f, maxHalf * 0.5f, maxHalf })
                    {
                        foreach (float bottom in new[] { 80f, 119.7f, 180f })
                        {
                            foreach (bool empty in new[] { false, true })
                            {
                                foreach (float stripWidth in new[] { 258f, 560f, 5000f })
                                {
                                    var bar = new BarMetrics { HasPortraits = true, MinX = w / 2f - half, MaxX = w / 2f + half, BottomY = bottom, PortraitHeight = 48f };
                                    foreach (bool open in new[] { false, true })
                                    {
                                        Placement p = ControlsLayout.Compute(w, h, bar, empty, stripWidth, 90f, 210f, open);
                                        CheckPlacement(p, w, h, bar, empty, stripWidth, open);
                                        cases++;
                                        compactSearch += p.SearchCompact ? 1 : 0;
                                        compactStrip += p.StripCompact ? 1 : 0;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            foreach ((int w, int h) in new[] { (799, 500), (800, 499), (640, 360), (320, 200) })
            {
                Placement p = ControlsLayout.Compute(w, h, new BarMetrics(), false, 258f, 90f, 210f);
                if (p.Visible)
                    throw new InvalidOperationException($"the controls show at {w}x{h}, below the 800x500 the colonist bar needs");
            }
            if (compactSearch == 0 || compactStrip == 0)
                throw new InvalidOperationException($"the sweep never reached the compact layouts (search {compactSearch}, strip {compactStrip}): it proves nothing about them");
            r.Notes.Add($"pure layout: {cases} cases, {compactSearch} with the search icon, {compactStrip} with the chips folded");
        }

        private static void CheckPlacement(Placement p, int w, int h, BarMetrics bar, bool empty, float stripWidth, bool open)
        {
            string at = $"{w}x{h}, bar {bar.MinX:F0}..{bar.MaxX:F0} to y {bar.BottomY:F0}, empty {empty}, strip {stripWidth}, open {open}";
            if (!p.Visible)
                throw new InvalidOperationException($"not visible at {at}");
            float limit = w - ControlsLayout.AlertsWidth;
            var all = new List<(string name, Rect rect)> { ("chips", p.Strip) };
            if (p.SearchFieldShown)
                all.Add(("search field", p.Search));
            if (p.SearchCompact)
                all.Add(("search icon", p.SearchIcon));
            if (empty)
                all.Add(("empty line", p.Empty));
            // The portraits, labels and frames end at BottomY and span MinX..MaxX (the layout is handed the unfiltered bar's footprint).
            var bounds = Rect.MinMaxRect(bar.MinX, 0f, bar.MaxX, bar.BottomY);
            foreach ((string name, Rect rect) in all)
            {
                if (rect.xMin < 0f || rect.yMin < 0f || rect.xMax > w || rect.yMax > h)
                    throw new InvalidOperationException($"the {name} {rect} leaves the screen at {at}");
                if (rect.xMax > limit + 0.01f)
                    throw new InvalidOperationException($"the {name} {rect} reaches into the alert column (x > {limit}) at {at}");
                if (name != "empty line" && rect.Overlaps(bounds) && !(name == "search icon" && false))
                    throw new InvalidOperationException($"the {name} {rect} overlaps the bar {bounds} at {at}");
            }
            if (p.Strip.yMin < bar.BottomY)
                throw new InvalidOperationException($"the chips {p.Strip} start above the bar's end {bar.BottomY} at {at}");
            for (int i = 0; i < all.Count; i++)
            {
                for (int j = i + 1; j < all.Count; j++)
                {
                    if (all[i].rect.Overlaps(all[j].rect))
                        throw new InvalidOperationException($"the {all[i].name} {all[i].rect} overlaps the {all[j].name} {all[j].rect} at {at}");
                }
            }
            if (stripWidth > w - ControlsLayout.AlertsWidth - 16f && !p.StripCompact)
                throw new InvalidOperationException($"{stripWidth} px of chips were not folded into the Filters button at {at}");
        }

        // ------------------------------------------------------------------------------------------ Phase 4 helpers

        private static FloatMenu OpenMenu() => Find.WindowStack.WindowOfType<FloatMenu>();

        /// <summary>The options of the FloatMenu that is open now (protected in vanilla: read through reflection).</summary>
        private static List<FloatMenuOption> OpenMenuOptions()
        {
            FloatMenu menu = OpenMenu();
            if (menu == null)
                throw new InvalidOperationException("no dropdown is open");
            return (List<FloatMenuOption>)HarmonyLib.AccessTools.Field(typeof(FloatMenu), "options").GetValue(menu);
        }

        private static string MenuLabels() => string.Join(" | ", OpenMenuOptions().Select(o => o.Label));

        /// <summary>Chooses the option the way a click does: its action runs, then the menu closes.</summary>
        private static void ChooseOption(string label)
        {
            FloatMenu menu = OpenMenu();
            FloatMenuOption option = OpenMenuOptions().FirstOrDefault(o => o.Label == label);
            if (option == null)
                throw new InvalidOperationException($"no option '{label}' in the dropdown [{MenuLabels()}]");
            option.action();
            Find.WindowStack.TryRemove(menu, doCloseSound: false);
        }

        private static void CloseAnyMenu()
        {
            FloatMenu menu;
            while ((menu = OpenMenu()) != null)
                Find.WindowStack.TryRemove(menu, doCloseSound: false);
        }

        /// <summary>The unfolded strip is what these scenarios click: at 800 px it has to fit with everything set (the folded menu has its own checks).</summary>
        private static void AssertStripUnfolded(string state)
        {
            Placement p = TheWindow().CurrentPlacement;
            if (p.StripCompact)
                throw new InvalidOperationException($"the strip is folded into the Filters button with {state}: the buttons these steps click do not exist (strip {p.Strip}, skill {TheWindow().SkillLabel}, sort {TheWindow().SortLabel})");
        }

        private static string LevelOption(int level) => (string)"ColonyCommand_LevelValue".Translate(level);

        // ----------------------------------------------------------------------------------------- Skill filter

        // Precondition: the cast staged and paused; the name token isolates it. Trigger: a real click on the Skill button, then
        // the dropdown's own options, Escape, and the level presets 0/5/10/15/Any.
        // Failure modes: a wrong dropdown (a skill missing, no way back to "No skill"), the level boundary off by one (>= vs >),
        // 0 treated as Any or as "available", a colonist who cannot do the skill (the corpse) passing, a skill alone hiding
        // people, a dropdown that keeps the search field focused, leaves the bar dirty, or does not close on Escape (FR-05)
        // — or whose Escape reaches the main menu.
        private static void SkillFilter()
        {
            var s = new Scenario("Skill filter", 4000000);
            var r = new Roster();
            Cast c = Stage(s, r);
            ViewStep(s, r, c, "the name token isolates the cast", () => ViewState.Query = Token, Letters(c, "ABCDEF"));
            s.WaitFrames(8);

            // The field holds the focus first: opening a dropdown must take it away (a click elsewhere), and leave the bar alone.
            FocusSearch(s, r);
            ClickStepAt(s, "click the Skill button", () => Center(TheWindow().SkillRect));
            s.ExpectWithin("a dropdown opened: 'No skill' and every skill of the game", 10, () => OpenMenu() != null, () => $"windows [{string.Join(", ", Find.WindowStack.Windows.Select(w => w.GetType().Name))}]")
                .Do("its options are right and the field lost the focus", () =>
                {
                    List<string> want = new[] { (string)"ColonyCommand_SkillNone".Translate() }
                        .Concat(DefDatabase<SkillDef>.AllDefsListForReading.Select(d => d.skillLabel.CapitalizeFirst())).ToList();
                    List<string> got = OpenMenuOptions().Select(o => o.Label).ToList();
                    if (!want.SequenceEqual(got))
                        throw new InvalidOperationException($"the dropdown holds [{string.Join(" | ", got)}], expected [{string.Join(" | ", want)}]");
                    if (TheWindow().SearchFocused)
                        throw new InvalidOperationException("the search field kept the focus while the dropdown opened");
                    if (BarProbe.IsDirty(Bar))
                        throw new InvalidOperationException("opening the dropdown left the bar dirty");
                });
            s.Do("count main menu requests from here", () => MenuRequests.Count = 0);
            InputStep(s, "press Escape with the dropdown open", "key Escape");
            s.ExpectWithin("Escape closed the dropdown and nothing else: no skill chosen, the bar is as it was, no main menu", 10, () => OpenMenu() == null,
                    () => $"still open: [{MenuLabels()}]")
                .Do("nothing changed", () =>
                {
                    if (ViewState.Skill != null)
                        throw new InvalidOperationException("Escape chose a skill");
                    if (MenuRequests.Count != 0)
                        throw new InvalidOperationException("the Escape that closed the dropdown also asked for the main menu");
                })
                .ExpectWithin("still the whole cast", 10, () => Shows(r, c, Letters(c, "ABCDEF")), () => r.Last);

            // Choosing a skill hands over to the level dropdown (a skill alone filters nobody).
            ClickStepAt(s, "click the Skill button again", () => Center(TheWindow().SkillRect));
            s.ExpectWithin("the dropdown is open", 10, () => OpenMenu() != null)
                .Do("choose the skill", () => ChooseOption(c.Skill.skillLabel.CapitalizeFirst()))
                .ExpectWithin("the level dropdown opened by itself: Any, 0+, 5+, 10+, 15+; the skill alone hides nobody", 10,
                    () => OpenMenu() != null && ViewState.Skill == c.Skill && Shows(r, c, Letters(c, "ABCDEF")), () => $"skill {ViewState.Skill?.defName}; {r.Last}")
                .Do("its presets are the spec's", () =>
                {
                    string want = string.Join(" | ", new[] { (string)"ColonyCommand_LevelMenuAny".Translate() }.Concat(new[] { 0, 5, 10, 15 }.Select(LevelOption)));
                    if (MenuLabels() != want)
                        throw new InvalidOperationException($"the level dropdown holds [{MenuLabels()}], expected [{want}]");
                });
            var levels = new (int level, string expect)[] { (0, "ABCDE"), (5, "ACD"), (10, "ACD"), (15, "D") };
            // 0+ keeps everybody who can do the skill (E is at level 0, the corpse is unavailable); 5+ drops B (4) and E; 10+ keeps
            // A and C (12) and D (18); 15+ only D.
            ChooseStep(s, "choose 0+", () => ChooseOption(LevelOption(0)));
            s.ExpectWithin("0+: everybody who can do the skill", 20, () => ViewState.MinLevel == 0 && Shows(r, c, Letters(c, "ABCDE")), () => $"min {ViewState.MinLevel}; {r.Last}");
            foreach ((int level, string expect) in levels.Skip(1))
            {
                int lv = level;
                string want = expect;
                s.Do($"the strip is unfolded with a skill and a level set", () => AssertStripUnfolded($"{c.Skill.defName} and a level"));
                ClickStepAt(s, $"click the Level button for {lv}+", () => Center(TheWindow().LevelRect));
                s.ExpectWithin("the level dropdown is open", 10, () => OpenMenu() != null)
                    .Do($"choose {lv}+", () => ChooseOption(LevelOption(lv)))
                    .ExpectWithin($"{lv}+: [{want}]", 20, () => ViewState.MinLevel == lv && Shows(r, c, Letters(c, want)), () => $"min {ViewState.MinLevel}; {r.Last}");
            }
            ClickStepAt(s, "click the Level button for Any", () => Center(TheWindow().LevelRect));
            s.ExpectWithin("the level dropdown is open", 10, () => OpenMenu() != null)
                .Do("choose Any level", () => ChooseOption((string)"ColonyCommand_LevelMenuAny".Translate()))
                .ExpectWithin("Any: the criterion is off again, the skill stays chosen, everybody shows", 20,
                    () => ViewState.MinLevel == ViewState.AnyLevel && ViewState.Skill == c.Skill && Shows(r, c, Letters(c, "ABCDEF")), () => $"min {ViewState.MinLevel}; {r.Last}");
            ClickStepAt(s, "click the Skill button, choose 'No skill'", () => Center(TheWindow().SkillRect));
            s.ExpectWithin("the dropdown is open", 10, () => OpenMenu() != null)
                .Do("choose 'No skill'", () => ChooseOption((string)"ColonyCommand_SkillNone".Translate()))
                .Do("the skill and its level are gone, and so is the Level button", () =>
                {
                    if (ViewState.Skill != null || ViewState.MinLevel != ViewState.AnyLevel)
                        throw new InvalidOperationException($"skill {ViewState.Skill?.defName}, level {ViewState.MinLevel}");
                })
                .WaitFrames(6)
                .Do("no Level button without a skill", () =>
                {
                    if (TheWindow().LevelRect.width > 0f)
                        throw new InvalidOperationException("the Level button shows although no skill is chosen");
                })
                .OnFinish(CloseAnyMenu);
            s.Start();
        }

        private static void ChooseStep(Scenario s, string label, Action choose) => s.Do(label, choose);

        // ----------------------------------------------------------------------------------------- Sort controls

        // Precondition: the cast staged and paused (no query: the map's own colonists interleave). Trigger: a real click on the
        // Sort button, its options, the direction button, the skill sort once a skill is chosen.
        // Failure modes: the skill sort offered with no skill chosen; a key that starts in the wrong direction; the direction
        // button not reversing (or shown while the order is vanilla); the order not matching the spec (Phase 2 oracle); Vanilla
        // not bringing the game's own bar back.
        private static void SortControls()
        {
            var s = new Scenario("Sort controls", 4000000);
            var r = new Roster();
            Cast c = Stage(s, r);
            s.WaitFrames(8);
            ClickStepAt(s, "click the Sort button", () => Center(TheWindow().SortRect));
            s.ExpectWithin("the sort dropdown is open", 10, () => OpenMenu() != null)
                .Do("Vanilla order, Name, Mood; no skill sort without a skill", () =>
                {
                    string want = string.Join(" | ", new[] { "ColonyCommand_SortMenuVanilla", "ColonyCommand_SortMenuName", "ColonyCommand_SortMenuMood" }.Select(k => (string)k.Translate()));
                    if (MenuLabels() != want)
                        throw new InvalidOperationException($"the sort dropdown holds [{MenuLabels()}], expected [{want}]");
                    if (TheWindow().DirectionRect.width > 0f)
                        throw new InvalidOperationException("the direction button shows while the order is the game's");
                })
                .Do("choose Name", () => ChooseOption((string)"ColonyCommand_SortMenuName".Translate()))
                .ExpectWithin("A-Z (case-insensitive)", 20, () => ViewState.Sort == SortKey.Name && !ViewState.Descending && Shows(r, c, () => c.Ordered("C", "E", "F", "D", "A", "B")),
                    () => $"sort {ViewState.Sort}, descending {ViewState.Descending}; {r.Last}")
                .WaitFrames(6)
                .Do("the direction button reads A-Z", () =>
                {
                    if (TheWindow().DirectionRect.width <= 0f || TheWindow().DirectionLabel != (string)"ColonyCommand_DirNameAsc".Translate())
                        throw new InvalidOperationException($"direction button '{TheWindow().DirectionLabel}' at {TheWindow().DirectionRect}");
                });
            ClickStepAt(s, "click the direction button", () => Center(TheWindow().DirectionRect));
            s.ExpectWithin("Z-A", 20, () => ViewState.Descending && Shows(r, c, () => c.Ordered("B", "A", "D", "F", "E", "C")), () => $"descending {ViewState.Descending}; {r.Last}");

            ClickStepAt(s, "click the Sort button", () => Center(TheWindow().SortRect));
            s.ExpectWithin("the sort dropdown is open", 10, () => OpenMenu() != null)
                .Do("choose Mood", () => ChooseOption((string)"ColonyCommand_SortMenuMood".Translate()))
                .ExpectWithin("a new key starts high to low (ties in vanilla order, the corpse last)", 20,
                    () => ViewState.Sort == SortKey.Mood && ViewState.Descending && Shows(r, c, () => c.Ordered("A", "CD", "B", "E", "F")),
                    () => $"sort {ViewState.Sort}, descending {ViewState.Descending}; {r.Last}");
            ClickStepAt(s, "click the direction button", () => Center(TheWindow().DirectionRect));
            s.ExpectWithin("low to high", 20, () => !ViewState.Descending && Shows(r, c, () => c.Ordered("E", "B", "CD", "A", "F")), () => $"descending {ViewState.Descending}; {r.Last}");

            s.Do("choose a skill (through the state: the Skill dropdown is proven by Skill filter)", () => ViewState.Skill = c.Skill)
                .WaitFrames(6)
                .Do("the strip is unfolded with a skill, a sort and its direction", () => AssertStripUnfolded("a skill and a sort"));
            ClickStepAt(s, "click the Sort button", () => Center(TheWindow().SortRect));
            s.ExpectWithin("the sort dropdown is open", 10, () => OpenMenu() != null)
                .Do("now there is a skill sort too", () =>
                {
                    string skillOption = (string)"ColonyCommand_SortMenuSkill".Translate(c.Skill.skillLabel);
                    if (!OpenMenuOptions().Any(o => o.Label == skillOption))
                        throw new InvalidOperationException($"no '{skillOption}' in [{MenuLabels()}]");
                    ChooseOption(skillOption);
                })
                .ExpectWithin("high to low in the chosen skill", 20,
                    () => ViewState.Sort == SortKey.Skill && ViewState.Descending && Shows(r, c, () => c.Ordered("D", "AC", "B", "E", "F")),
                    () => $"sort {ViewState.Sort}, descending {ViewState.Descending}; {r.Last}");

            ClickStepAt(s, "click the Sort button", () => Center(TheWindow().SortRect));
            s.ExpectWithin("the sort dropdown is open", 10, () => OpenMenu() != null)
                .Do("choose Vanilla order", () => ChooseOption((string)"ColonyCommand_SortMenuVanilla".Translate()))
                .ExpectWithin("the game's own bar is back, with nothing else set", 20, () => ViewState.Sort == SortKey.Vanilla && VanillaBack(r), () => $"sort {ViewState.Sort}; {r.Last}")
                .WaitFrames(6)
                .Do("and the direction button is gone", () =>
                {
                    if (TheWindow().DirectionRect.width > 0f)
                        throw new InvalidOperationException("the direction button still shows");
                })
                .OnFinish(CloseAnyMenu);
            s.Start();
        }

        // ---------------------------------------------------------------------------------------- Reset control

        // Precondition: the cast staged and paused; a query, a status, a skill with a level, a sort and a direction are set.
        // Trigger: a real click on the Reset button.
        // Failure modes: Reset shown when nothing is set, or missing when something is; Reset leaving any one of the six
        // pieces of state (or the search field's text) behind; the bar not exactly vanilla afterwards — checked the hard way:
        // a real drag on the bar must reorder again (it is a no-op while a view is active), with a control that it does not.
        // The compact Filters menu carries the same Reset.
        private static void ResetControl()
        {
            var s = new Scenario("Reset control", 4000000);
            var r = new Roster();
            Cast c = Stage(s, r);
            Dictionary<Pawn, int> orders = null;
            s.Do("remember every display order", () => orders = r.Vanilla.Where(e => e.pawn != null).ToDictionary(e => e.pawn, e => e.pawn.playerSettings.displayOrder))
                .OnFinish(() => RestoreOrders(orders));
            s.WaitFrames(8)
                .Do("nothing is set: no Reset button, and no Reset in the folded menu", () =>
                {
                    if (TheWindow().StripResetRect.width > 0f)
                        throw new InvalidOperationException("the Reset button shows although nothing is set");
                    if (TheWindow().FoldedMenuOptions().Any(o => o.Label == (string)"ColonyCommand_Reset".Translate()))
                        throw new InvalidOperationException("the folded menu offers Reset although nothing is set");
                })
                .Do("set a query, a status, a skill, a level, a sort and a direction", () =>
                {
                    ViewState.Query = "fx";
                    ViewState.Status = StatusFilter.Injured;
                    ViewState.Skill = c.Skill;
                    ViewState.MinLevel = 5;
                    ViewState.Sort = SortKey.Mood;
                    ViewState.Descending = true;
                })
                .ExpectWithin("the view holds: injured, skill >= 5 (A, D), by mood high to low", 20, () => Shows(r, c, () => c.Ordered("A", "D")), () => r.Last)
                .WaitFrames(8);
            DragStep(s, "control: a drag while the view is active", 0, 1);
            s.Do("it reordered nothing: the view's drag is a no-op", () =>
            {
                if (BarProbe.ReorderCalls != 0)
                    throw new InvalidOperationException($"{BarProbe.ReorderCalls} Reorder call(s) reached the vanilla writer from the view");
            });
            s.Do("the strip is unfolded with everything set", () => AssertStripUnfolded("a query, a status, a skill, a level, a sort and a direction"));
            ClickStepAt(s, "click the Reset button", () => Center(TheWindow().StripResetRect));
            s.ExpectWithin("Reset: all six pieces of state at their defaults, the field empty, the vanilla bar exactly back", 20, () =>
                    !ViewState.AnythingSet && ViewState.Query == "" && ViewState.Status == StatusFilter.None && ViewState.Skill == null
                    && ViewState.MinLevel == ViewState.AnyLevel && ViewState.Sort == SortKey.Vanilla && !ViewState.Descending
                    && TheWindow().Search.filter.Text == "" && VanillaBack(r),
                    () => $"anything set {ViewState.AnythingSet}, query '{ViewState.Query}', {ViewState.Status}, {ViewState.Skill?.defName}, level {ViewState.MinLevel}, {ViewState.Sort}, desc {ViewState.Descending}, field '{TheWindow().Search.filter.Text}'; {r.Last}")
                .WaitFrames(8)
                .Do("and the Reset button is gone again", () =>
                {
                    if (TheWindow().StripResetRect.width > 0f)
                        throw new InvalidOperationException("the Reset button still shows");
                });
            DragStep(s, "after Reset: a real drag on the bar", 0, 2);
            s.Do("it reaches ColonistBar.Reorder again: the bar is the game's own, drag included", () =>
                {
                    if (BarProbe.ReorderCalls < 1)
                        throw new InvalidOperationException("the drag after Reset did not reach ColonistBar.Reorder: the bar is not exactly vanilla (or input injection failed: the control above did not prove it)");
                    r.Notes.Add($"drag after Reset: {BarProbe.ReorderCalls} Reorder call(s)");
                    RestoreOrders(orders);
                })
                .ExpectWithin("vanilla order back after restoring the display orders", 20, () => VanillaBack(r), () => r.Last);

            // The compact strip's menu does the same.
            s.Do("set a status and a sort, then run Reset from the folded menu's options", () =>
                {
                    ViewState.Status = StatusFilter.Drafted;
                    ViewState.Sort = SortKey.Name;
                })
                .ExpectWithin("the view holds", 20, () => Shows(r, c, () => "B"), () => r.Last)
                .Do("the folded menu lists Reset, chooses it, and everything is cleared", () =>
                {
                    FloatMenuOption option = TheWindow().FoldedMenuOptions().FirstOrDefault(o => o.Label == (string)"ColonyCommand_Reset".Translate());
                    if (option == null)
                        throw new InvalidOperationException("the folded menu has no Reset");
                    option.action();
                    if (ViewState.AnythingSet)
                        throw new InvalidOperationException("the folded menu's Reset left something set");
                })
                .ExpectWithin("vanilla again", 20, () => VanillaBack(r), () => r.Last)
                .Start();
        }
    }
}
