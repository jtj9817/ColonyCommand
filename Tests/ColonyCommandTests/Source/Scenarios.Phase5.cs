using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ColonyCommand;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ColonyCommandTests
{
    // Phase 5: the view and the chip counts follow the live game. Nothing a pawn does tells the colonist bar that a
    // colonist was drafted, hurt, went idle, cheered up or learned a skill, so the adapter looks again every 0.25 s of real
    // time. Every step below changes the GAME (never a control) and waits for the bar to follow by itself; the game is
    // paused throughout, so only that refresh can explain a change (it runs on real time).
    public static partial class Scenarios
    {
        /// <summary>
        /// The bound a change is held to: one refresh period of real time, plus what the frames cost. The adapter looks every
        /// 0.25 s but can only look in a frame, and the step only sees it in a frame, so a long frame (the first frames of
        /// the world view, a map switch) delays both: two of the longest frame seen while waiting, and 0.1 s of slack.
        /// </summary>
        private static float LiveBound(float longestFrame) => BarAdapter.RefreshSeconds + 2f * longestFrame + 0.1f;

        private sealed class Live
        {
            public float Worst;
            public int Steps;
            public string WorstText = "";
        }

        private static bool Has(Pawn pawn) => pawn != null && ShownPawns().Contains(pawn);

        /// <summary>
        /// Changes the game with <paramref name="mutate"/> and waits for <paramref name="done"/> without any control being
        /// touched. The expected state must not hold before the change (else the step proves nothing), the view state's version
        /// must not move, and the adapter's colonist count must agree with the bar it wrote.
        /// </summary>
        private static void LiveWait(Scenario s, Live live, string label, Action mutate, Func<bool> done, Func<string> why,
            Action extra = null, Action before = null)
        {
            int version = 0, polls = 0;
            float t0 = 0f, longestFrame = 0f;
            float notReadySeconds0 = 0f;
            Func<bool> watched = () =>
            {
                polls++;
                longestFrame = Mathf.Max(longestFrame, Time.unscaledDeltaTime);
                return done();
            };
            s.Do($"{label}: change the game and touch no control", () =>
                {
                    before?.Invoke();
                    if (done())
                        throw new InvalidOperationException($"{label}: the expected state already holds before the change: the step would prove nothing");
                    version = ViewState.Version;
                    notReadySeconds0 = AutoTest.NotReadySeconds;
                    t0 = Time.realtimeSinceStartup;
                    mutate();
                })
                .ExpectWithin($"{label}: the bar follows by itself", 5, watched, why)
                .Do($"{label}: within the bound, no control used", () =>
                {
                    // A long event (vanilla runs one for about a second when the world view first opens) stops the game's own UI
                    // update, and the mod with it (BarAdapter.GameReady): that time is not the refresh's to answer for.
                    float away = AutoTest.NotReadySeconds - notReadySeconds0;
                    float took = Time.realtimeSinceStartup - t0 - away, bound = LiveBound(longestFrame);
                    string text = $"{label}: {took:F2} s over {polls} frames, longest frame {longestFrame * 1000f:F0} ms" +
                        (away > 0.01f ? $", not counting {away:F2} s of a long event" : "");
                    if (took > live.Worst)
                    {
                        live.Worst = took;
                        live.WorstText = text;
                    }
                    live.Steps++;
                    if (took > bound)
                        throw new InvalidOperationException($"{text}: the bar followed later than {bound:F2} s (one refresh period of {BarAdapter.RefreshSeconds:F2} s plus the frames)");
                    if (ViewState.Version != version)
                        throw new InvalidOperationException($"{label}: the view state changed during the step: the refresh did not do this alone");
                    extra?.Invoke();
                });
        }

        /// <summary>A live change judged on the cast: the bar shows these cast members in this order, and <paramref name="also"/> holds for anybody else.</summary>
        private static void LiveStep(Scenario s, Roster r, Cast c, Live live, string label, Action mutate, Func<string> expected,
            Func<bool> also = null, Action extra = null)
        {
            LiveWait(s, live, label, mutate, () => Shows(r, c, expected) && (also == null || also()), () => r.Last, () =>
            {
                if (BarAdapter.Shown != ShownPawns().Count)
                    throw new InvalidOperationException($"{label}: the adapter says {BarAdapter.Shown} colonists are shown, the bar holds {ShownPawns().Count}: the empty state and the bar disagree");
                extra?.Invoke();
            });
        }

        /// <summary>A chip count that follows a change in the game while nothing is filtered (the bar itself is not touched).</summary>
        private static void LiveCountsStep(Scenario s, Live live, string label, Func<RosterCounts, int> metric, int delta, Action mutate)
        {
            int target = 0;
            LiveWait(s, live, label, mutate, () => metric(BarAdapter.Counts) == target,
                () => $"the count is {metric(BarAdapter.Counts)}, expected {target}",
                before: () => target = metric(BarAdapter.Counts) + delta);
        }

        private static void AssertLiveCounts(Roster r, string label)
        {
            var pawns = r.Vanilla.Where(e => e.pawn != null).Select(e => e.pawn).Where(p => Oracle.Name(p, Token)).ToList();
            RosterCounts got = BarAdapter.Counts;
            int drafted = pawns.Count(Oracle.Drafted), injured = pawns.Count(Oracle.Injured), idle = pawns.Count(Predicates.Idle);
            if (got.All != pawns.Count || got.Drafted != drafted || got.Injured != injured || got.Idle != idle)
                throw new InvalidOperationException(
                    $"{label}: chip counts all {got.All} / drafted {got.Drafted} / injured {got.Injured} / idle {got.Idle}, the game has {pawns.Count} / {drafted} / {injured} / {idle}");
        }

        /// <summary>Over 3 s of real time the adapter looks again about every 0.25 s: a handful of times, not once per frame, and not never.</summary>
        private static void ThrottleSteps(Scenario s, Roster r, string label, bool filtered)
        {
            float t0 = 0f;
            int f0 = 0;
            s.Do($"{label}: count the adapter's re-evaluations", () =>
                {
                    RefreshProbe.Start();
                    t0 = Time.realtimeSinceStartup;
                    f0 = Time.frameCount;
                })
                .ExpectWithin($"{label}: 3 s of real time pass", 15, () => Time.realtimeSinceStartup - t0 >= 3f)
                .Do($"{label}: a handful of re-evaluations, not one per frame", () =>
                {
                    float secs = Time.realtimeSinceStartup - t0;
                    int frames = Time.frameCount - f0;
                    int calls = filtered ? RefreshProbe.Build : RefreshProbe.Count;
                    int other = filtered ? RefreshProbe.Count : RefreshProbe.Build;
                    float expected = secs / BarAdapter.RefreshSeconds;
                    if (frames < 30)
                        throw new InvalidOperationException($"{label}: only {frames} frames in {secs:F1} s: too slow to tell a throttle from a re-evaluation on every frame");
                    if (calls < expected * 0.6f || calls > expected * 1.4f + 1f)
                        throw new InvalidOperationException($"{label}: {calls} re-evaluations in {secs:F1} s ({frames} frames), expected about {expected:F0}");
                    if (other != 0)
                        throw new InvalidOperationException($"{label}: {other} calls of the other kind: a filtered view must rebuild and a plain bar must only count");
                    r.Notes.Add($"{label}: {calls} in {secs:F1} s over {frames} frames");
                });
        }

        /// <summary>Nothing of the game may be left in the mod's statics (a stale pawn, map or old game); a control proves the scan sees what it looks for.</summary>
        private static void AssertHoldsNothing(string label)
        {
            List<string> held = StaleReferences.Scan();
            if (held.Count > 0)
                throw new InvalidOperationException($"{label}: the mod's statics hold: {string.Join("; ", held.Take(5))}");
        }

        private static void ScanControl(Pawn pawn)
        {
            var scratch = (List<ColonistBar.Entry>)typeof(BarAdapter).GetField("view", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            scratch.Add(new ColonistBar.Entry(pawn, Find.CurrentMap, 0));
            try
            {
                if (StaleReferences.Scan().Count == 0)
                    throw new InvalidOperationException("control: a pawn left in the adapter's scratch list was not found: the scan is blind");
            }
            finally
            {
                scratch.Clear();
            }
        }

        // ------------------------------------------------------------------------------------------ Live refresh

        // Precondition: the cast staged and paused (A idle+injured, B drafted, C idle, D injured, E a scar, F a corpse), a view
        // on the token, then no view. Trigger: the GAME changes (draft, injure, heal, idle, mood, skill, a colonist joins, leaves
        // or dies), no control is touched, and the clock is paused, so only the adapter's own real-time look can notice.
        // Failure modes: counts or view stale after a change (never refreshed, or only on the next click); a re-evaluation on
        // every frame (not throttled); the view losing the empty case (nobody passes) or the way back out of it; a joining
        // colonist missing, a leaver or a dead colonist kept; a pawn, map or old game left in a static of the mod.
        private static void LiveRefresh()
        {
            var s = new Scenario("Live refresh", 6000000);
            var r = new Roster();
            Cast c = Stage(s, r);
            var live = new Live();
            Pawn g = null;
            s.OnFinish(() =>
            {
                if (g != null && !g.Destroyed)
                    g.Destroy();
            });
            s.Do("E carries its triple name: a pawn is generated below, and a single name breaks vanilla's relation generation", () => c.E.Name = c.TripleNameOfE);
            Func<bool> noEntries = () => BarProbe.CachedEntries(Bar).Count == 0 && BarProbe.CachedLocs(Bar).Count == 0;
            void EmptyAndConsistent(string label)
            {
                if (!noEntries())
                    throw new InvalidOperationException($"{label}: the bar still holds {BarProbe.CachedEntries(Bar).Count} entries and {BarProbe.CachedLocs(Bar).Count} locations");
            }

            // 1. How often the adapter looks, with a view and without.
            ViewStep(s, r, c, "view: the token", () => ViewState.Query = Token, Letters(c, "ABCDEF"));
            ThrottleSteps(s, r, "a view in place", filtered: true);
            s.Do("no criteria", () => ViewState.Reset())
                .ExpectWithin("the vanilla bar", 20, () => VanillaBack(r), () => r.Last);
            ThrottleSteps(s, r, "nothing filtered", filtered: false);

            // 2. Chip counts follow while nothing is filtered, and the bar is not touched.
            LiveCountsStep(s, live, "nothing filtered, C is drafted", n => n.Drafted, +1, () => Fixtures.Draft(c.C));
            LiveCountsStep(s, live, "nothing filtered, C is injured", n => n.Injured, +1, () => Fixtures.Injure(c.C));
            LiveCountsStep(s, live, "nothing filtered, A goes back to work", n => n.Idle, -1, () => Fixtures.SetJobTag(c.A, JobTag.Misc));
            s.Do("only the counts moved: the bar is still the vanilla bar; put C and A back", () =>
                {
                    if (BarAdapter.Applied)
                        throw new InvalidOperationException("the adapter wrote a view while nothing is filtered");
                    Fixtures.Undraft(c.C);
                    Fixtures.Heal(c.C);
                    Fixtures.SetJobTag(c.A, JobTag.Idle);
                })
                .ExpectWithin("the vanilla bar", 20, () => VanillaBack(r), () => r.Last);

            // 3. Drafted, including the empty view and the way out of it.
            ViewStep(s, r, c, "Drafted view of the cast", () => { ViewState.Query = Token; ViewState.Status = StatusFilter.Drafted; }, Letters(c, "B"));
            LiveStep(s, r, c, live, "C is drafted", () => Fixtures.Draft(c.C), Letters(c, "BC"), null, () => AssertLiveCounts(r, "C is drafted"));
            LiveStep(s, r, c, live, "B is undrafted", () => Fixtures.Undraft(c.B), Letters(c, "C"), null, () => AssertLiveCounts(r, "B is undrafted"));
            LiveStep(s, r, c, live, "C is undrafted: nobody passes", () => Fixtures.Undraft(c.C), Is(""), null, () =>
            {
                EmptyAndConsistent("nobody is drafted");
                AssertLiveCounts(r, "nobody is drafted");
            });
            LiveStep(s, r, c, live, "B is drafted again: the empty view fills up", () => Fixtures.Draft(c.B), Letters(c, "B"));

            // 4. Injured: a scar never counts, a fresh wound does, a healed one stops.
            ViewStep(s, r, c, "Injured view", () => ViewState.Status = StatusFilter.Injured, Letters(c, "AD"));
            LiveStep(s, r, c, live, "A is healed", () => Fixtures.Heal(c.A), Letters(c, "D"), null, () => AssertLiveCounts(r, "A is healed"));
            LiveStep(s, r, c, live, "B is wounded", () => Fixtures.Injure(c.B), Letters(c, "BD"), null, () => AssertLiveCounts(r, "B is wounded"));

            // 5. Idle: the bar's own rule, on a clock that has passed day 1.
            ViewStep(s, r, c, "Idle view", () =>
                {
                    ViewState.Status = StatusFilter.Idle;
                    Fixtures.SetJobTag(c.A, JobTag.Idle);
                    Fixtures.SetJobTag(c.C, JobTag.Idle);
                    Fixtures.SetJobTag(c.D, JobTag.Misc);
                    Fixtures.SetJobTag(c.E, JobTag.Misc);
                    Fixtures.SetJobTag(c.B, JobTag.DraftedOrder);
                }, Letters(c, "AC"));
            LiveStep(s, r, c, live, "A starts working", () => Fixtures.SetJobTag(c.A, JobTag.Misc), Letters(c, "C"), null, () => AssertLiveCounts(r, "A starts working"));
            LiveStep(s, r, c, live, "E has nothing to do", () => Fixtures.SetJobTag(c.E, JobTag.Idle), Letters(c, "CE"), null, () => AssertLiveCounts(r, "E has nothing to do"));

            // 6. Mood re-sorts the view.
            ViewStep(s, r, c, "Mood, high to low", () =>
                {
                    ViewState.Status = StatusFilter.None;
                    ViewState.Sort = SortKey.Mood;
                    ViewState.Descending = true;
                }, () => c.Ordered("A", "CD", "B", "E", "F"));
            LiveStep(s, r, c, live, "B becomes the happiest", () => Fixtures.SetMood(c.B, 0.99f), () => c.Ordered("B", "A", "CD", "E", "F"));
            LiveStep(s, r, c, live, "E cheers up", () => Fixtures.SetMood(c.E, 0.80f), () => c.Ordered("B", "A", "E", "CD", "F"));

            // 7. A skill level crossing the minimum.
            ViewStep(s, r, c, "skill >= 15", () =>
                {
                    ViewState.Sort = SortKey.Vanilla;
                    ViewState.Descending = false;
                    ViewState.Skill = c.Skill;
                    ViewState.MinLevel = 15;
                }, Letters(c, "D"));
            LiveStep(s, r, c, live, "A reaches level 16", () => Fixtures.SetSkill(c.A, c.Skill, 16), Letters(c, "AD"));
            LiveStep(s, r, c, live, "D drops to level 3", () => Fixtures.SetSkill(c.D, c.Skill, 3), Letters(c, "A"));
            LiveStep(s, r, c, live, "B reaches level 17", () => Fixtures.SetSkill(c.B, c.Skill, 17), Letters(c, "AB"));

            // 8. The roster itself: a colonist joins, leaves, dies.
            ViewStep(s, r, c, "back to the token view: the whole cast", () =>
                {
                    ViewState.Reset();
                    ViewState.Query = Token;
                }, Letters(c, "ABCDEF"));
            LiveStep(s, r, c, live, "a colonist joins the map", () => g = Fixtures.SpawnNamed(s, new NameTriple("Gale", "Gale", "Fxwind")),
                Letters(c, "ABCDEF"), () => Has(g));
            LiveStep(s, r, c, live, "the colonist leaves the map", () =>
                {
                    g.DeSpawn();
                    Find.WorldPawns.PassToWorld(g);
                }, Letters(c, "ABCDEF"), () => !Has(g));
            s.Do("the mod's statics hold no pawn, map or old game (control: the scan finds a pawn planted in a scratch list)", () =>
            {
                ScanControl(c.A);
                AssertHoldsNothing("after joins and leaves");
            });
            ViewStep(s, r, c, "Drafted view again (B is the drafted one)", () => ViewState.Status = StatusFilter.Drafted, Letters(c, "B"));
            LiveStep(s, r, c, live, "B dies: nobody is drafted, the view is empty and consistent", () =>
                {
                    c.B.Kill(null);
                    Messages.Clear();
                    if (c.B.Corpse == null)
                        throw new InvalidOperationException("B left no corpse");
                    s.Track(c.B.Corpse);
                }, Is(""), null, () =>
                {
                    EmptyAndConsistent("B died");
                    AssertLiveCounts(r, "B died");
                });
            ViewStep(s, r, c, "status off: the dead colonist is still in the roster (found by name only)", () => ViewState.Status = StatusFilter.None, Letters(c, "ABCDEF"));
            s.Do("the mod's statics hold no pawn, map or old game after a death", () => AssertHoldsNothing("after a death"))
                .Do("reset; the refresh speed", () =>
                {
                    ViewState.Reset();
                    r.Notes.Add($"{live.Steps} live changes, the slowest: {live.WorstText}");
                })
                .ExpectWithin("no view is written", 20, () => !BarAdapter.Applied)
                .Start();
        }

        // ------------------------------------------------------------------------ Live refresh across views

        // Precondition: the cast staged and paused, a second player map with two colonists (Fxaway), a Drafted view of the
        // token in place. Trigger: colonists are drafted and undrafted on the map the camera is NOT looking at, on the second
        // map while the camera is there, and in the world view; then the second map goes, the game is saved, goes to the main
        // menu and is loaded again, and a colonist is drafted in the new game.
        // Failure modes: counts or view only following the colonists of the map in view; the world view or a map switch
        // leaving them stale; the main menu or a load leaving the mod holding a pawn, a map or the finished game; the refresh
        // dead after a load (the adapter's state surviving into a game whose bar is a different object).
        private static void LiveRefreshAcrossViews()
        {
            var s = new Scenario("Live refresh across views", 8000000);
            var r = new Roster();
            Cast c = Stage(s, r);
            var live = new Live();
            Map home = null, second = null;
            Settlement settlement = null;
            var away = new List<Pawn>();
            Pawn y1 = null, y2 = null;
            void TearDownSecondMap()
            {
                if (WorldRendererUtility.WorldSelected)
                    CameraJumper.TryHideWorld();
                if (home != null && !home.Disposed && Current.Game.CurrentMap != home)
                    Current.Game.CurrentMap = home;
                foreach (Pawn p in away.Where(p => !p.Destroyed))
                    p.Destroy();
                away.Clear();
                if (settlement != null && !settlement.Destroyed)
                    Find.WorldObjects.Remove(settlement);
                settlement = null;
                second = null;
                y1 = y2 = null;
            }
            s.OnFinish(TearDownSecondMap);
            s.Do("E carries its triple name: pawns are generated below", () => c.E.Name = c.TripleNameOfE)
                .Do("settle a second home map and spawn two colonists on it", () =>
                {
                    home = Find.CurrentMap;
                    PlanetTile tile = TileFinder.RandomSettlementTileFor(Faction.OfPlayer, mustBeAutoChoosable: false,
                        extraValidator: t => !Find.WorldObjects.AnyWorldObjectAt(t));
                    settlement = SettleUtility.AddNewHome(tile, Faction.OfPlayer);
                    second = GetOrGenerateMapUtility.GetOrGenerateMap(tile, new IntVec3(60, 1, 60), null);
                    var names = new[] { new NameTriple("Yarrow", "Yarrow", "Fxaway"), new NameTriple("Yelena", "Yelena", "Fxaway") };
                    foreach (NameTriple name in names)
                    {
                        Pawn p = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                        p.Name = name;
                        GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(second.Center, second, 8), second);
                        away.Add(p);
                    }
                    y1 = away[0];
                    y2 = away[1];
                });
            CaptureSteps(s, r, 8);   // the vanilla roster again, now with two maps
            ViewStep(s, r, c, "Drafted view of the token: B only", () => { ViewState.Query = Token; ViewState.Status = StatusFilter.Drafted; },
                Letters(c, "B"), () =>
                {
                    if (Has(y1) || Has(y2))
                        throw new InvalidOperationException("a colonist of the second map is shown although nobody there is drafted");
                });

            // The second map is not the one in view: its colonists still count.
            LiveStep(s, r, c, live, "Y1 is drafted on the second map, the camera is on the home map", () => Fixtures.Draft(y1), Letters(c, "B"), () => Has(y1) && !Has(y2));
            s.Do("the camera goes to the second map", () => CameraJumper.TryJump(second.Center, second))
                .ExpectWithin("the second map is the current map", 20, () => Find.CurrentMap == second);
            LiveStep(s, r, c, live, "Y1 is undrafted while the camera is on the second map", () => Fixtures.Undraft(y1), Letters(c, "B"), () => !Has(y1) && !Has(y2));
            LiveStep(s, r, c, live, "Y2 is drafted while the camera is on the second map", () => Fixtures.Draft(y2), Letters(c, "B"), () => !Has(y1) && Has(y2));

            // The world view.
            s.Do("the world view goes up", () => CameraJumper.TryShowWorld())
                .ExpectWithin("the world is showing", 20, () => WorldRendererUtility.WorldSelected);
            LiveStep(s, r, c, live, "B is undrafted in the world view", () => Fixtures.Undraft(c.B), Is(""), () => Has(y2) && !Has(y1));
            LiveStep(s, r, c, live, "Y1 is drafted in the world view", () => Fixtures.Draft(y1), Is(""), () => Has(y1) && Has(y2));
            s.Do("back to the home map", () =>
                {
                    CameraJumper.TryHideWorld();
                    CameraJumper.TryJump(home.Center, home);
                })
                .ExpectWithin("the home map is showing", 20, () => !WorldRendererUtility.WorldSelected && Find.CurrentMap == home)
                .Do("no criteria", () => ViewState.Reset())
                .ExpectWithin("the vanilla bar", 20, () => VanillaBack(r), () => r.Last);

            // Chip counts see the other map while nothing is filtered.
            LiveCountsStep(s, live, "nothing filtered, Y1 on the other map is undrafted", n => n.Drafted, -1, () => Fixtures.Undraft(y1));

            // The second map goes; a filtered view is in place when the game goes to the main menu and comes back.
            s.Do("the second map is removed", TearDownSecondMap)
                .ExpectWithin("one map is left", 20, () => Find.Maps.Count == 1, () => $"{Find.Maps.Count} maps");
            string idC = null;
            var castIds = new List<string>();   // ThingIDs outlive the load; the pawn objects do not
            s.Do("remember the cast by ThingID: the pawn objects are replaced by the load", () =>
            {
                idC = c.C.ThingID;
                castIds.AddRange(c.All.Select(p => p.ThingID));
            });
            ViewStep(s, r, c, "a Drafted view of the token is in place before the save", () => { ViewState.Query = Token; ViewState.Status = StatusFilter.Drafted; },
                Is(""));
            s.Do("the mod holds nothing of the game while a view is in place", () =>
                {
                    ScanControl(c.A);
                    AssertHoldsNothing("a view in place");
                })
                .Do("save, go to the main menu and load that save again", () =>
                {
                    GameDataSaveLoader.SaveGame(Scenario.SaveName);
                    MenuProbe.Begin(Scenario.SaveName);
                    GenScene.GoToMainMenu();
                })
                .ExpectWithin("the game is back from the main menu", 180, () => MenuProbe.Finished, () => $"{MenuProbe.Frames} menu frames")
                .Expect("speed restored", Scenario.Resume)
                .Expect("paused again", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    return Find.TickManager.CurTimeSpeed == TimeSpeed.Paused;
                })
                .Do("the main menu held nothing of the finished game", () =>
                {
                    if (MenuProbe.HeldOnMenu == null)
                        throw new InvalidOperationException($"no scan ran on the main menu ({MenuProbe.Frames} menu frames)");
                    if (MenuProbe.HeldOnMenu.Count > 0)
                        throw new InvalidOperationException($"on the main menu the mod's statics held: {string.Join("; ", MenuProbe.HeldOnMenu.Take(5))}");
                    r.Notes.Add("the main menu: nothing held");
                })
                .ExpectWithin("the new game: the seam is on, the state is at its defaults and nothing of the old game is held", 30, () =>
                {
                    if (!BarAdapter.Enabled)
                        throw new InvalidOperationException("the seam is switched off after the load");
                    if (ViewState.AnythingSet)
                    {
                        r.Last = "the view state survived the load";
                        return false;
                    }
                    AssertHoldsNothing("after the load");
                    return true;
                }, () => r.Last);

            // The refresh is alive in the new game (a different Game, ColonistBar and map objects).
            Func<Pawn> newC = () => Scenario.ById(idC) as Pawn ?? throw new InvalidOperationException("colonist C is not in the reloaded game");
            LiveCountsStep(s, live, "the reloaded game, nothing filtered: C is drafted", n => n.Drafted, +1, () => Fixtures.Draft(newC()));
            Func<bool> viewIsC = () =>
            {
                Pawn p = newC();
                List<string> shown = ShownPawns().Select(x => x.ThingID).Where(castIds.Contains).ToList();
                return BarAdapter.Applied && BarAdapter.AppliedVersion == ViewState.Version && !BarProbe.IsDirty(Bar)
                    && shown.Count == 1 && shown[0] == p.ThingID;
            };
            s.Do("a Drafted view of the token in the reloaded game", () =>
                {
                    ViewState.Query = Token;
                    ViewState.Status = StatusFilter.Drafted;
                })
                .ExpectWithin("the view shows C, the only drafted colonist of the cast", 20, viewIsC);
            LiveWait(s, live, "the reloaded game, C is undrafted", () => Fixtures.Undraft(newC()),
                () => BarAdapter.Applied && BarAdapter.AppliedVersion == ViewState.Version && ShownPawns().All(x => !castIds.Contains(x.ThingID)),
                () => $"the bar shows [{string.Join(", ", ShownPawns().Select(x => x.LabelShort))}]");
            s.Do("reset", () =>
                {
                    ViewState.Reset();
                    r.Notes.Add($"{live.Steps} live changes, the slowest: {live.WorstText}");
                })
                .Start();
        }
    }
}
