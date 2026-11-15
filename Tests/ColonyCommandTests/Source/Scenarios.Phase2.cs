using System;
using System.Collections.Generic;
using System.Linq;
using ColonyCommand;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ColonyCommandTests
{
    // Phase 2: the real view (ViewState, Predicates, RosterView, BarAdapter). Each scenario spawns the same small cast of
    // named colonists (A..F, below), holds the game paused so nothing drifts while it asserts, and judges the bar by what
    // it actually holds (BarProbe reads the private lists; the getters would recache and hide a stale view).
    //
    // Assertions are made on the fixtures only (the quicktest map's own colonists are random and interleave in the bar):
    // expectations name fixtures by letter, in the order the bar must show them. Where a query could also reach a random
    // colonist, an independent restatement of the rule (Oracle, below) is compared as well.
    public static partial class Scenarios
    {
        /// <summary>
        /// The cast, all free colonists near the center:
        /// A Quillon "Quill" Fxmarsh   injured, idle,            skill 12, mood .90
        /// B Xandrel "Zan" Fxquaid     drafted,                  skill 4,  mood .60
        /// C Brisk "Brisk" Fxoakley    idle,                     skill 12, mood .75
        /// D Quinlan "Lan" Fxdrummond  injured,                  skill 18, mood .75
        /// E single name "BZfxsolo"    a scar only (permanent),  skill 0,  mood .50
        /// F Wraithe "Ghost" Fxhaunt   dead, still wounded and drafted when it died: a corpse entry in the bar
        /// Ties are deliberate (mood C=D at .75, skill A=C at 12): they must resolve by vanilla position, in both directions.
        /// </summary>
        private sealed class Cast
        {
            public Pawn A, B, C, D, E, F;
            public SkillDef Skill;
            public int OriginalTicks = -1;
            public Roster R;
            /// <summary>E's name while pawns are generated (see StageCast); restored when the scenario ends.</summary>
            public Name TripleNameOfE;
            public readonly Dictionary<Pawn, string> Names = new Dictionary<Pawn, string>();
            public Dictionary<Pawn, bool> IdleIcon;

            public IEnumerable<Pawn> All => new[] { A, B, C, D, E, F };
            public IEnumerable<Pawn> Living => new[] { A, B, C, D, E };

            public string Letter(Pawn p) =>
                p == A ? "A" : p == B ? "B" : p == C ? "C" : p == D ? "D" : p == E ? "E" : p == F ? "F" : null;

            /// <summary>The fixtures among these entries, as letters, in entry order.</summary>
            public string Letters(IEnumerable<ColonistBar.Entry> entries) =>
                string.Concat(entries.Where(e => e.pawn != null).Select(e => Letter(e.pawn)).Where(l => l != null));

            /// <summary>The letters of <paramref name="set"/> in the order vanilla shows them.</summary>
            public string InVanillaOrder(string set) => Letters(R.Vanilla.Where(e => e.pawn != null)
                .Where(e => Letter(e.pawn) != null && set.Contains(Letter(e.pawn))));

            /// <summary>Tie groups in the required order; inside a group, vanilla order decides.</summary>
            public string Ordered(params string[] tieGroups) => string.Concat(tieGroups.Select(InVanillaOrder));
        }

        private const string Token = "fx";   // every cast name contains it: a query that isolates the cast

        // ------------------------------------------------------------------------------------------- staging

        private static Cast Stage(Scenario s, Roster r)
        {
            var c = new Cast { R = r };
            s.OnFinish(() =>
                {
                    ViewState.Reset();
                    BarAdapter.ProbeView = false;
                    BarProbe.Stop();
                    IdleProbe.Stop();
                    Find.Selector.ClearSelection();
                    if (c.E != null && c.TripleNameOfE != null)
                        c.E.Name = c.TripleNameOfE;   // it may outlive the scenario in the world pawns: keep it a triple for vanilla
                    if (c.OriginalTicks >= 0)
                        Find.TickManager.DebugSetTicksGame(c.OriginalTicks);
                })
                .Summary(() => string.Join("; ", r.Notes.Append(r.Last).Where(x => x.Length > 0)));
            s.Expect("the game is paused: nothing ticks while the state is staged and read", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    return Find.TickManager.CurTimeSpeed == TimeSpeed.Paused;
                })
                .Do("no criteria, probe off, then stage the cast", () =>
                {
                    ViewState.Reset();
                    BarAdapter.ProbeView = false;
                    StageCast(s, c);
                })
                .ExpectWithin("the vanilla roster is captured", 20, () => CaptureVanilla(r, 6), () => r.Last)
                .Do("all six cast members are in it, the dead one as a corpse entry", () =>
                {
                    foreach (Pawn p in c.All)
                    {
                        if (!r.Vanilla.Any(e => e.pawn == p))
                            throw new InvalidOperationException($"{c.Letter(p)} ({p.LabelShort}) is not in the vanilla bar");
                    }
                    if (!c.F.Dead)
                        throw new InvalidOperationException("F is not dead");
                    r.Notes.Add($"{r.Vanilla.Count(e => e.pawn != null)} colonists in the bar, skill under test: {c.Skill.defName}, cast in vanilla order [{c.InVanillaOrder("ABCDEF")}]");
                });
            return c;
        }

        private static void StageCast(Scenario s, Cast c)
        {
            c.OriginalTicks = Find.TickManager.TicksGame;
            c.A = Fixtures.SpawnNamed(s, new NameTriple("Quillon", "Quill", "Fxmarsh"));
            c.B = Fixtures.SpawnNamed(s, new NameTriple("Xandrel", "Zan", "Fxquaid"));
            c.C = Fixtures.SpawnNamed(s, new NameTriple("Brisk", "Brisk", "Fxoakley"));
            c.D = Fixtures.SpawnNamed(s, new NameTriple("Quinlan", "Lan", "Fxdrummond"));
            // Vanilla pawn generation can pick any existing pawn as a parent and casts its name to NameTriple
            // (PawnRelationWorker_Parent.ResolveMyName): a NameSingle pawn that exists while another pawn is generated, now or in a
            // later scenario of the same run, throws an InvalidCastException. So E is a triple until all generation is done.
            c.E = Fixtures.SpawnNamed(s, c.TripleNameOfE = new NameTriple("Solo", "BZfxsolo", "Fx"));
            c.F = Fixtures.SpawnNamed(s, new NameTriple("Wraithe", "Ghost", "Fxhaunt"));
            // F first: its death must not leave anything on the others (they are staged after it, the game stays paused).
            Fixtures.Injure(c.F);
            Fixtures.Draft(c.F);
            c.E.Name = new NameSingle("BZfxsolo");   // nothing generates a pawn from here on
            c.F.Kill(null);
            if (c.F.Corpse == null)
                throw new InvalidOperationException("F left no corpse");
            s.Track(c.F.Corpse);
            // The death put a message on screen. Vanilla draws messages as windows, and the bar ignores the mouse under one
            // (Mouse.IsOver is false beneath a window): a message over a portrait swallows a real click or drag.
            Messages.Clear();
            c.Skill = Fixtures.CommonSkill(c.Living);
            if (c.Skill == null)
                throw new InvalidOperationException("no skill is usable by all five living cast members");
            Fixtures.Injure(c.A);
            Fixtures.Injure(c.D);
            Fixtures.Injure(c.E, permanent: true);
            Fixtures.Draft(c.B);
            int[] levels = { 12, 4, 12, 18, 0 };
            // Well above the mental-break thresholds: a low mood makes vanilla post "Critical alert: Major break risk" messages, which
            // are windows over the first portraits and swallow real clicks and drags.
            float[] moods = { 0.90f, 0.60f, 0.75f, 0.75f, 0.50f };
            Pawn[] living = c.Living.ToArray();
            for (int i = 0; i < living.Length; i++)
            {
                Fixtures.SetSkill(living[i], c.Skill, levels[i]);
                Fixtures.SetMood(living[i], moods[i]);
                Fixtures.SetJobTag(living[i], JobTag.Misc);
            }
            Fixtures.SetJobTag(c.A, JobTag.Idle);
            Fixtures.SetJobTag(c.C, JobTag.Idle);
            Fixtures.SetJobTag(c.B, JobTag.DraftedOrder);
            Find.TickManager.DebugSetTicksGame(c.OriginalTicks + 120000);   // two game days in: the bar's idle icon needs one
            foreach (Pawn p in c.All)
                c.Names[p] = p.Name.ToStringFull;
            Find.Selector.ClearSelection();
        }

        // ---------------------------------------------------------------------------------------------- oracles

        /// <summary>The rules restated independently of <see cref="Predicates"/> (a typo in one shows against the other).</summary>
        private static class Oracle
        {
            public static bool Name(Pawn p, string query)
            {
                string q = query.Trim();
                if (q.Length == 0)
                    return true;
                var parts = new List<string> { p.LabelShort, p.Name?.ToStringFull };
                if (p.Name is NameTriple t)
                    parts.AddRange(new[] { t.First, t.Nick, t.Last });
                if (p.Name is NameSingle one)
                    parts.Add(one.Name);
                return parts.Any(x => x != null && x.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            public static bool Drafted(Pawn p) => !p.Dead && p.drafter != null && p.drafter.Drafted;

            public static bool Injured(Pawn p) =>
                !p.Dead && p.health.hediffSet.hediffs.Any(h => h is Hediff_Injury && !h.IsPermanent());
        }

        private static string Names(IEnumerable<Pawn> pawns) => string.Join(", ", pawns.Select(p => p.LabelShort));

        /// <summary>The pawns the bar holds (entries with a pawn), straight from its private list.</summary>
        private static List<Pawn> ShownPawns() => BarProbe.CachedEntries(Bar).Where(e => e.pawn != null).Select(e => e.pawn).ToList();

        /// <summary>
        /// Settled and showing exactly these cast members in this order: the bar is not dirty, the layout is consistent,
        /// and the cast members it holds read <paramref name="expected"/> (evaluated now: it may depend on the vanilla order).
        /// </summary>
        private static bool Shows(Roster r, Cast c, Func<string> expected)
        {
            ColonistBar bar = Bar;
            if (BarProbe.IsDirty(bar))
            {
                r.Last = "the bar is dirty (repair pending)";
                return false;
            }
            List<ColonistBar.Entry> cached = BarProbe.CachedEntries(bar);
            string problem = LayoutProblem(bar, cached.Count);
            if (problem != null)
                throw new InvalidOperationException(problem);
            string shown = c.Letters(cached), want = expected();
            if (shown != want)
            {
                r.Last = $"the bar shows [{shown}], expected [{want}]";
                return false;
            }
            r.Last = "";
            return true;
        }

        /// <summary>Sets the criteria, then waits until the bar shows exactly this (a step pair).</summary>
        private static void ViewStep(Scenario s, Roster r, Cast c, string label, Action set, Func<string> expected, Action extra = null)
        {
            s.Do($"{label}: set the criteria", set)
                .ExpectWithin($"{label}: the bar shows the expected cast", 20, () => Shows(r, c, expected), () => r.Last);
            if (extra != null)
                s.Do($"{label}: more checks", extra);
        }

        /// <summary>Every pawn passing <paramref name="passes"/> is shown and nobody else is (all of the vanilla roster, cast or not).</summary>
        private static void AssertShownEquals(Roster r, string label, Func<Pawn, bool> passes)
        {
            var want = new HashSet<Pawn>(r.Vanilla.Where(e => e.pawn != null && passes(e.pawn)).Select(e => e.pawn));
            var got = new HashSet<Pawn>(ShownPawns());
            if (!want.SetEquals(got))
                throw new InvalidOperationException(
                    $"{label}: expected [{Names(want)}], the bar shows [{Names(got)}]; missing [{Names(want.Except(got))}], extra [{Names(got.Except(want))}]");
        }

        /// <summary>The pawns of the bar that are not cast members all pass <paramref name="passes"/> (nothing leaks through).</summary>
        private static void AssertNoLeaks(Cast c, string label, Func<Pawn, bool> passes)
        {
            List<Pawn> leaked = ShownPawns().Where(p => c.Letter(p) == null && !passes(p)).ToList();
            if (leaked.Count > 0)
                throw new InvalidOperationException($"{label}: pawns shown that fail the criteria: [{Names(leaked)}]");
        }

        private static void AssertEntrySetIsVanilla(Roster r, string label)
        {
            List<ColonistBar.Entry> cached = BarProbe.CachedEntries(Bar);
            var want = r.Vanilla.Select(e => e.pawn?.ThingID ?? "-").OrderBy(x => x).ToList();
            var got = cached.Select(e => e.pawn?.ThingID ?? "-").OrderBy(x => x).ToList();
            if (!want.SequenceEqual(got))
                throw new InvalidOperationException($"{label}: the entries are not the vanilla set: {got.Count} shown, {want.Count} in vanilla");
        }

        private static Func<string> Letters(Cast c, string set) => () => c.InVanillaOrder(set);

        private static Func<string> Is(string exact) => () => exact;

        // ------------------------------------------------------------------------------------------ Name search

        // Precondition: the cast (A..F) staged and paused, no criteria, B selected. Trigger: queries typed as a user would
        // (case, partial, first/nick/last/single name, full name, spaces), then whitespace only, then a query nobody matches.
        // Failure modes: a wrong match on case, partial, a name part or whitespace; a dead colonist missing from the
        // results; the no-match view leaving a broken bar (draw locations out of step, a hittable hidden pawn, an
        // exception); the search changing names or selection (FR-03); the vanilla roster not coming back.
        private static void NameSearch()
        {
            var s = new Scenario("Name search", 2000000);
            var r = new Roster();
            Cast c = Stage(s, r);
            s.Do("select B (a hidden colonist must stay selected)", () => Find.Selector.Select(c.B, playSound: false));
            var cases = new (string label, string query, string expect)[]
            {
                ("nick, lower case", "quill", "A"),
                ("nick, upper case", "QUILL", "A"),
                ("leading and trailing spaces", "  quill  ", "A"),
                ("partial first names", "qui", "AD"),
                ("inside a name, not a prefix", "uil", "A"),
                ("partial last name", "oakley", "C"),
                ("partial last name, upper case", "FXDRUM", "D"),
                ("a nick that is no part of the first name", "zan", "B"),
                ("partial first name", "xandr", "B"),
                ("a single name, partial", "fxsolo", "E"),
                ("first and last name together", "brisk fxoak", "C"),
                ("a token every cast name has", Token, "ABCDEF"),
                ("a dead colonist by nick", "ghost", "F"),
            };
            foreach ((string label, string query, string expect) in cases)
            {
                string q = query;
                ViewStep(s, r, c, $"query '{q}' ({label})", () => ViewState.Query = q, Letters(c, expect),
                    () => AssertShownEquals(r, $"query '{q}'", p => Oracle.Name(p, q)));
            }
            s.Do("whitespace only: the search is not active", () => ViewState.Query = "   ")
                .ExpectWithin("the bar is the untouched vanilla bar", 20, () => VanillaBack(r), () => r.Last)
                .Do("empty: still vanilla", () => ViewState.Query = "")
                .ExpectWithin("the bar is the untouched vanilla bar again", 20, () => VanillaBack(r), () => r.Last);

            s.Do("a query that matches nobody", () =>
                {
                    BarProbe.Start();
                    ViewState.Query = "zzznomatchzzz";
                })
                .ExpectWithin("the bar holds no entry and no draw location", 20,
                    () => !BarProbe.IsDirty(Bar) && BarProbe.CachedEntries(Bar).Count == 0 && BarProbe.CachedLocs(Bar).Count == 0,
                    () => $"{BarProbe.CachedEntries(Bar).Count} entries, {BarProbe.CachedLocs(Bar).Count} locations")
                .ExpectWithin("15 repaints drawn", 30, () => BarProbe.Repaints >= 15, () => $"{BarProbe.Repaints} repaints")
                .Do("every draw held 0 entries and 0 locations, nobody can be hit", () =>
                {
                    AssertDraws("no match", "", 15);
                    for (int x = 0; x < UI.screenWidth; x += 3)
                    {
                        for (int y = 0; y < 200; y += 3)
                        {
                            Thing at = Bar.ColonistOrCorpseAt(new Vector2(x, y));
                            if (at != null)
                                throw new InvalidOperationException($"{at.LabelShort} can be hit at ({x}, {y}) although nobody matches");
                        }
                    }
                    r.Notes.Add("no match: 0 entries, 0 locations, 15+ repaints");
                })
                .Do("clear the query", () => ViewState.Query = "")
                .ExpectWithin("the vanilla roster, order and layout are back", 20, () => VanillaBack(r), () => r.Last)
                .Do("names and selection were never touched", () =>
                {
                    foreach (Pawn p in c.All)
                    {
                        if (p.Name.ToStringFull != c.Names[p])
                            throw new InvalidOperationException($"{c.Letter(p)}'s name changed from '{c.Names[p]}' to '{p.Name.ToStringFull}'");
                    }
                    if (Find.Selector.SingleSelectedThing != c.B)
                        throw new InvalidOperationException($"the selection is [{string.Join(", ", Find.Selector.SelectedObjects.Select(o => o.ToString()))}], expected only B");
                })
                .Start();
        }

        // ----------------------------------------------------------------------------------------- Status chips

        // Precondition: the cast staged and paused, day 2, no criteria. Trigger: the vanilla bar is observed drawing its
        // idle icon (the oracle); then each status, one after the other; then a real draft followed by single ticks.
        // Failure modes: Drafted/Injured/Idle disagreeing with the game (a scar or a corpse counted as injured, a drafted
        // colonist idle); Predicates.Idle disagreeing with the icon the bar itself draws (control: day 0 draws none);
        // more than one status active at once; chip counts wrong or changed by the active status.
        private static void StatusChips()
        {
            var s = new Scenario("Status chips", 4000000);
            var r = new Roster();
            Cast c = Stage(s, r);

            // 1. The oracle is the bar's own icon, at day 2 and (control) at day 0.
            IdleIconSteps(s, r, c, "day 2", () => c.OriginalTicks + 120000);
            s.Do("day 2: Predicates.Idle equals the bar's icon for every colonist; A and C are idle, the rest are not", () =>
            {
                AssertIdleAgreesWithIcon(r, c);
                foreach (Pawn p in c.All)
                {
                    bool want = p == c.A || p == c.C;
                    if (IdleProbe.Drawn[p] != want || Predicates.Idle(p) != want)
                        throw new InvalidOperationException($"{c.Letter(p)}: idle icon {IdleProbe.Drawn[p]}, predicate {Predicates.Idle(p)}, expected {want}");
                }
                c.IdleIcon = new Dictionary<Pawn, bool>(IdleProbe.Drawn);
                r.Notes.Add($"idle icon oracle agrees for {c.IdleIcon.Count} colonists ({c.IdleIcon.Count(kv => kv.Value)} idle)");
            });
            IdleIconSteps(s, r, c, "control, day 0", () => 1000);
            s.Do("control, day 0: the bar draws no idle icon and the predicate agrees, so the day 2 result was not vacuous", () =>
            {
                AssertIdleAgreesWithIcon(r, c);
                if (IdleProbe.Drawn.Values.Any(v => v))
                    throw new InvalidOperationException("an idle icon was drawn on day 0");
                Find.TickManager.DebugSetTicksGame(c.OriginalTicks + 120000);
            });

            // 2. The statuses, one after the other, isolated to the cast by the name token.
            s.Do("isolate the cast with the name token", () => IdleProbe.Stop());
            ViewStep(s, r, c, "Drafted", () => { ViewState.Query = Token; ViewState.Status = StatusFilter.Drafted; }, Letters(c, "B"),
                () => AssertShownEquals(r, "Drafted", p => Oracle.Name(p, Token) && Oracle.Drafted(p)));
            s.Do("Drafted: the counts ignore the active status and match the game", () => AssertCounts(r, c, Token));
            ViewStep(s, r, c, "Injured (a scar and a corpse's wounds do not count)", () => ViewState.Status = StatusFilter.Injured, Letters(c, "AD"),
                () => AssertShownEquals(r, "Injured", p => Oracle.Name(p, Token) && Oracle.Injured(p)));
            ViewStep(s, r, c, "Idle", () => ViewState.Status = StatusFilter.Idle, Letters(c, "AC"),
                () => AssertShownEquals(r, "Idle", p => Oracle.Name(p, Token) && c.IdleIcon[p]));
            s.Do("Idle: the counts ignore the active status and match the game", () => AssertCounts(r, c, Token));
            ViewStep(s, r, c, "back to Drafted: one status at a time, not a union", () => ViewState.Status = StatusFilter.Drafted, Letters(c, "B"));
            ViewStep(s, r, c, "status off: the whole cast again", () => ViewState.Status = StatusFilter.None, Letters(c, "ABCDEF"));

            // 3. A real draft: the bar's own rule (lastJobTag) takes a drafted colonist out of Idle.
            s.Do("no criteria", () => ViewState.Reset())
                .ExpectWithin("the vanilla bar", 20, () => VanillaBack(r), () => r.Last)
                .Do("draft C (idle) through the game's own setter", () =>
                {
                    if (!Predicates.Idle(c.C))
                        throw new InvalidOperationException("C is not idle before it is drafted: nothing to prove");
                    Fixtures.Draft(c.C);
                })
                .ExpectWithin("a few single ticks later the think tree tagged C as drafted", 10, () =>
                {
                    Find.TickManager.DoSingleTick();
                    return c.C.mindState.lastJobTag == JobTag.DraftedOrder;
                }, () => $"lastJobTag {c.C.mindState.lastJobTag}, job {c.C.CurJob?.def.defName ?? "none"}");
            IdleIconSteps(s, r, c, "after drafting C", null);
            s.Do("a drafted colonist is not idle, by the predicate and by the bar's icon", () =>
            {
                AssertIdleAgreesWithIcon(r, c);
                if (Predicates.Idle(c.C) || IdleProbe.Drawn[c.C])
                    throw new InvalidOperationException($"C is drafted but idle: predicate {Predicates.Idle(c.C)}, icon {IdleProbe.Drawn[c.C]}");
                if (!Predicates.Drafted(c.C))
                    throw new InvalidOperationException("C is not drafted");
            })
                .Start();
        }

        /// <summary>Resets the view, optionally sets the game tick (a day-0 or day-2 clock), records the bar's idle icons and waits until every colonist was drawn.</summary>
        private static void IdleIconSteps(Scenario s, Roster r, Cast c, string label, Func<int> setTicks)
        {
            s.Do($"{label}: record the bar's idle icons", () =>
                {
                    ViewState.Reset();
                    if (setTicks != null)
                        Find.TickManager.DebugSetTicksGame(setTicks());
                    IdleProbe.Start();
                })
                .ExpectWithin($"{label}: the vanilla bar is back and has drawn every colonist", 20, () =>
                {
                    if (!VanillaBack(r))
                        return false;
                    List<Pawn> undrawn = r.Vanilla.Where(e => e.pawn != null && !IdleProbe.Drawn.ContainsKey(e.pawn)).Select(e => e.pawn).ToList();
                    r.Last = undrawn.Count > 0 ? $"no icon pass yet for [{Names(undrawn)}]" : "";
                    return undrawn.Count == 0;
                }, () => r.Last)
                .WaitFrames(10);
        }

        private static void AssertIdleAgreesWithIcon(Roster r, Cast c)
        {
            if (IdleProbe.IconPasses < r.Vanilla.Count(e => e.pawn != null))
                throw new InvalidOperationException($"the oracle saw only {IdleProbe.IconPasses} icon passes: it is not watching the bar");
            List<Pawn> wrong = r.Vanilla.Where(e => e.pawn != null)
                .Select(e => e.pawn).Where(p => Predicates.Idle(p) != IdleProbe.Drawn[p]).ToList();
            if (wrong.Count > 0)
                throw new InvalidOperationException(
                    $"Predicates.Idle disagrees with the bar's idle icon for [{string.Join(", ", wrong.Select(p => $"{p.LabelShort}: predicate {Predicates.Idle(p)}, icon {IdleProbe.Drawn[p]}"))}]");
        }

        /// <summary>Counts as the pipeline reports them for this query, compared with the game: ignoring the active status and skill.</summary>
        private static void AssertCounts(Roster r, Cast c, string query)
        {
            RosterCounts got = BarAdapter.Counts;
            var pawns = r.Vanilla.Where(e => e.pawn != null).Select(e => e.pawn).ToList();
            List<Pawn> named = pawns.Where(p => Oracle.Name(p, query)).ToList();
            var want = new RosterCounts
            {
                Eligible = pawns.Count,
                All = named.Count,
                Drafted = named.Count(Oracle.Drafted),
                Injured = named.Count(Oracle.Injured),
                Idle = named.Count(p => c.IdleIcon[p]),
            };
            string text = $"got eligible {got.Eligible}, all {got.All}, drafted {got.Drafted}, injured {got.Injured}, idle {got.Idle}; " +
                $"expected {want.Eligible}, {want.All}, {want.Drafted}, {want.Injured}, {want.Idle}";
            if (got.Eligible != want.Eligible || got.All != want.All || got.Drafted != want.Drafted || got.Injured != want.Injured || got.Idle != want.Idle)
                throw new InvalidOperationException($"chip counts differ: {text}");
            if (want.All == 6 && !(want.Drafted == 1 && want.Injured == 2 && want.Idle == 2))
                throw new InvalidOperationException($"the cast should hold 1 drafted, 2 injured, 2 idle: {text}");
            r.Notes.Add($"counts {want.All} named / {want.Drafted} drafted / {want.Injured} injured / {want.Idle} idle of {want.Eligible}");
        }

        // -------------------------------------------------------------------------------------- Combined filter

        // Precondition: the cast staged and paused. Trigger: name search, status and skill together, then the All chip.
        // Failure modes: AND broken (any criterion ignored or OR-ed), the skill minimum off by one or 0 treated as Any or
        // an unavailable skill (the corpse) passing, a zero-result combination breaking the bar, All dropping the search.
        private static void CombinedFilter()
        {
            var s = new Scenario("Combined filter", 2000000);
            var r = new Roster();
            Cast c = Stage(s, r);
            void NoLeaks(string label) => AssertNoLeaks(c, label, p => Oracle.Name(p, ViewState.Query));

            ViewStep(s, r, c, "name 'qui' + Idle", () => { ViewState.Query = "qui"; ViewState.Status = StatusFilter.Idle; }, Letters(c, "A"), () => NoLeaks("qui + Idle"));
            ViewStep(s, r, c, "name 'qui' + skill >= 15", () =>
                {
                    ViewState.Status = StatusFilter.None;
                    ViewState.Skill = c.Skill;
                    ViewState.MinLevel = 15;
                }, Letters(c, "D"), () => NoLeaks("qui + skill 15"));
            ViewStep(s, r, c, "token + Injured + skill >= 15", () =>
                {
                    ViewState.Query = Token;
                    ViewState.Status = StatusFilter.Injured;
                }, Letters(c, "D"), () => NoLeaks("token + Injured + skill 15"));
            ViewStep(s, r, c, "token + Idle + skill >= 12", () =>
                {
                    ViewState.Status = StatusFilter.Idle;
                    ViewState.MinLevel = 12;
                }, Letters(c, "AC"), () => NoLeaks("token + Idle + skill 12"));
            ViewStep(s, r, c, "token + Idle + skill >= 15: nobody passes", () => ViewState.MinLevel = 15, Is(""),
                () => AssertNoLeaks(c, "nobody passes", p => false));
            s.Do("the empty combination left a consistent bar", () =>
            {
                if (BarProbe.CachedEntries(Bar).Count != 0 || BarProbe.CachedLocs(Bar).Count != 0)
                    throw new InvalidOperationException("the bar still holds entries or locations");
            });
            ViewStep(s, r, c, "token + skill >= 0 (no status): the living, the corpse is unavailable", () =>
                {
                    ViewState.Status = StatusFilter.None;
                    ViewState.MinLevel = 0;
                }, Letters(c, "ABCDE"), () => NoLeaks("skill 0"));
            ViewStep(s, r, c, "token + skill 'Any': the criterion is off", () => ViewState.MinLevel = ViewState.AnyLevel, Letters(c, "ABCDEF"),
                () => NoLeaks("skill any"));
            ViewStep(s, r, c, "token + Injured + skill >= 15, then the All chip: status and skill go, the search stays", () =>
                {
                    ViewState.Status = StatusFilter.Injured;
                    ViewState.MinLevel = 15;
                }, Letters(c, "D"));
            ViewStep(s, r, c, "after the All chip", () => ViewState.ClearStatusAndSkill(), Letters(c, "ABCDEF"),
                () =>
                {
                    if (ViewState.Query != Token)
                        throw new InvalidOperationException($"the All chip changed the query to '{ViewState.Query}'");
                    NoLeaks("after All");
                });
            s.Do("reset", () => ViewState.Reset())
                .ExpectWithin("the vanilla bar", 20, () => VanillaBack(r), () => r.Last)
                .Start();
        }

        // ------------------------------------------------------------------------------------- Sort and restore

        // Precondition: the cast staged and paused, all colonists shown (no filter). Trigger: each sort key in both
        // directions, a skill sort without a skill, then back to Vanilla.
        // Failure modes: ties not resolved by vanilla position (in both directions), unavailable values (the corpse's mood
        // and skill) not last in both directions, a case-sensitive name sort, entries lost or invented by a sort-only
        // view, the vanilla order and layout not restored, playerSettings.displayOrder written.
        private static void SortAndRestore()
        {
            var s = new Scenario("Sort and restore", 2000000);
            var r = new Roster();
            Cast c = Stage(s, r);
            Dictionary<Pawn, int> orders = null;
            s.Do("remember every display order", () => orders = r.Vanilla.Where(e => e.pawn != null).ToDictionary(e => e.pawn, e => e.pawn.playerSettings.displayOrder))
                .OnFinish(() => RestoreOrders(orders));
            void Entries(string label) => AssertEntrySetIsVanilla(r, label);

            ViewStep(s, r, c, "name A-Z (case-insensitive: BZfxsolo comes after Brisk)", () => ViewState.Sort = SortKey.Name, () => c.Ordered("C", "E", "F", "D", "A", "B"), () => Entries("name A-Z"));
            ViewStep(s, r, c, "name Z-A", () => ViewState.Descending = true, () => c.Ordered("B", "A", "D", "F", "E", "C"), () => Entries("name Z-A"));
            ViewStep(s, r, c, "mood, low to high (C and D tie: vanilla order; the corpse is unavailable, last)", () =>
                {
                    ViewState.Sort = SortKey.Mood;
                    ViewState.Descending = false;
                }, () => c.Ordered("E", "B", "CD", "A", "F"), () => Entries("mood asc"));
            ViewStep(s, r, c, "mood, high to low (tie still in vanilla order, the corpse still last)", () => ViewState.Descending = true,
                () => c.Ordered("A", "CD", "B", "E", "F"), () => Entries("mood desc"));
            s.Do("a skill sort without a skill: the bar stays vanilla", () =>
                {
                    ViewState.Sort = SortKey.Skill;
                    if (ViewState.Skill != null)
                        throw new InvalidOperationException("a skill is chosen already");
                })
                .ExpectWithin("the untouched vanilla bar", 20, () => VanillaBack(r), () => r.Last)
                .Do("choose a skill: the criterion stays off (minimum level 'Any'), so nobody is hidden", () => ViewState.Skill = c.Skill)
                .ExpectWithin("skill, high to low (A and C tie at 12: vanilla order; the corpse is unavailable, last)", 20,
                    () => Shows(r, c, () => c.Ordered("D", "AC", "B", "E", "F")), () => r.Last)
                .Do("skill sort holds every vanilla entry", () => Entries("skill desc"));
            ViewStep(s, r, c, "skill, low to high", () => ViewState.Descending = false, () => c.Ordered("E", "B", "AC", "D", "F"), () => Entries("skill asc"));
            ViewStep(s, r, c, "mood again after the skill sort (same input, same order)", () =>
                {
                    ViewState.Sort = SortKey.Mood;
                    ViewState.Descending = false;
                }, () => c.Ordered("E", "B", "CD", "A", "F"));
            s.Do("back to the vanilla order", () => ViewState.Sort = SortKey.Vanilla)
                .ExpectWithin("vanilla order, layout and scale are exactly back (the skill stays chosen, it is no criterion)", 20, () => VanillaBack(r), () => r.Last)
                .Do("no display order was written", () =>
                {
                    if (OrdersChanged(orders))
                        throw new InvalidOperationException("a display order changed");
                    ViewState.Reset();
                })
                .ExpectWithin("the vanilla bar after Reset", 20, () => VanillaBack(r), () => r.Last)
                .Start();
        }

        // ----------------------------------------------------------------------------------------- Bar integrity

        // Precondition: the cast staged and paused; a view of two colonists (token + Injured, sorted by name) in place.
        // Trigger: the bar is dirtied every frame (from Update and from OnGUI), hit tests over the top band, then a real
        // right-button drag, each on the vanilla bar first as a control.
        // Failure modes: a flash of the full roster or draw locations out of step with entries after a dirty (an
        // IndexOutOfRange in the bar's draw loop); a hidden colonist that can still be hit; a drag in the view reaching
        // ColonistBar.Reorder and rewriting playerSettings.displayOrder.
        private static void BarIntegrity()
        {
            var s = new Scenario("Bar integrity", 4000000);
            var r = new Roster();
            Cast c = Stage(s, r);
            Dictionary<Pawn, int> orders = null;
            s.Do("remember every display order, restore them when the scenario ends", () =>
                orders = r.Vanilla.Where(e => e.pawn != null).ToDictionary(e => e.pawn, e => e.pawn.playerSettings.displayOrder))
                .OnFinish(() => RestoreOrders(orders));

            s.Do("clear the drag samples", () => DragSamples.Clear());
            DragStep(s, "control, real drag on the vanilla bar", 0, 2, SampleDrag);
            s.Do("control: the drag reached ColonistBar.Reorder, so the drag below is a real test", () =>
                {
                    if (BarProbe.ReorderCalls < 1)
                        throw new InvalidOperationException("the real drag never reached ColonistBar.Reorder: input injection is ineffective; " + InputDiagnostics());
                    r.Notes.Add($"control drag: {BarProbe.ReorderCalls} Reorder call(s)");
                    RestoreOrders(orders);
                })
                .ExpectWithin("vanilla roster back", 20, () => VanillaBack(r), () => r.Last);

            ViewStep(s, r, c, "the view: token + Injured, sorted by name", () =>
                {
                    ViewState.Query = Token;
                    ViewState.Status = StatusFilter.Injured;
                    ViewState.Sort = SortKey.Name;
                }, Is("DA"));
            s.Do("this view is the one every phase below must keep", () =>
            {
                r.Expected = BarProbe.Describe(BarProbe.CachedEntries(Bar));
                if (BarProbe.CachedEntries(Bar).Count != 2 || BarProbe.CachedLocs(Bar).Count != 2)
                    throw new InvalidOperationException($"{BarProbe.CachedEntries(Bar).Count} entries and {BarProbe.CachedLocs(Bar).Count} locations, expected 2 and 2");
            });
            DirtyPhase(s, r, "control, no hooks", 40, lateUpdate: false, windowHooks: false, dirtyInUpdate: true, dirtyAfterRepaint: false, bad =>
            {
                if (bad.Count == 0)
                    throw new InvalidOperationException("the detector saw no flash although every apply hook was removed: the clean results below would prove nothing");
            });
            DirtyPhase(s, r, "all hooks, dirty from Update and from OnGUI", 60, lateUpdate: true, windowHooks: true, dirtyInUpdate: true, dirtyAfterRepaint: true, bad =>
            {
                if (BarProbe.DirtyFired < 10)
                    throw new InvalidOperationException($"the OnGUI dirty hook fired only {BarProbe.DirtyFired} times");
                if (bad.Count > 0)
                    throw new InvalidOperationException($"{bad.Count} draws were wrong with every hook active, first: {bad[0]}");
            });
            s.Do("hit tests: each slot is its own colonist, every other colonist is unreachable", () => HitSweep(r));
            DragStep(s, "real drag on the view", 0, 1);
            s.Do("the drag in the view changed nothing", () =>
                {
                    if (BarProbe.ReorderCalls != 0)
                        throw new InvalidOperationException($"{BarProbe.ReorderCalls} Reorder call(s) reached the vanilla writer");
                    if (OrdersChanged(orders))
                        throw new InvalidOperationException("a display order changed");
                })
                .ExpectWithin("the view is still in place", 20, () => ViewInPlace(r), () => r.Last)
                .Do("reset", () => ViewState.Reset())
                .ExpectWithin("the vanilla bar", 20, () => VanillaBack(r), () => r.Last)
                .Start();
        }

        /// <summary>Samples of what the vanilla drag widget and the window stack look like while xdotool drags (diagnosis of a control that did not register).</summary>
        private static readonly List<string> DragSamples = new List<string>();

        private static void SampleDrag()
        {
            Type widget = typeof(ReorderableWidget);
            object Field(string name) => HarmonyLib.AccessTools.Field(widget, name)?.GetValue(null);
            Vector2 mouse = UI.MousePositionOnUIInverted;
            Window under = Find.WindowStack.GetWindowAt(mouse);
            string line = $"mouse ({mouse.x:F0},{mouse.y:F0}) drag {Field("draggingReorderable")} begun {Field("dragBegun")} clicked {Field("clicked")} " +
                $"released {Field("released")} reorderables {Field("lastFrameReorderableCount")} under {(under == null ? "none" : under.GetType().Name + under.windowRect)}";
            if (DragSamples.Count == 0 || DragSamples[DragSamples.Count - 1].Substring(DragSamples[DragSamples.Count - 1].IndexOf(' ')) != line.Substring(line.IndexOf(' ')))
                DragSamples.Add($"f{Time.frameCount} {line}");
        }

        private static string InputDiagnostics()
        {
            string events = string.Join(", ", BarProbe.Draws.GroupBy(d => d.type).Select(g => $"{g.Key} x{g.Count()}"));
            string windows = string.Join(", ", Find.WindowStack.Windows.Select(w => w.GetType().Name));
            Window under = Find.WindowStack.GetWindowAt(UI.MousePositionOnUIInverted);
            var live = HarmonyLib.AccessTools.Field(typeof(Messages), "liveMessages")?.GetValue(null) as System.Collections.IEnumerable;
            string messages = live == null ? "?" : string.Join(" / ", live.Cast<Message>().Select(m => m.text));
            return $"live messages [{messages}]; drag samples [{string.Join(" | ", DragSamples)}]; window under the mouse [{(under == null ? "none" : under.GetType().Name + under.windowRect)}]; events seen by the bar [{events}]; windows [{windows}]; scale {BarProbe.CachedScale(Bar):F2}; screen {UI.screenWidth}x{UI.screenHeight}; " +
                $"paused {Find.TickManager.Paused}; locations {string.Join(" ", BarProbe.CachedLocs(Bar).Take(4).Select(v => $"({v.x:F0},{v.y:F0})"))}";
        }

        // --------------------------------------------------------------------------------------- View reset on load

        // Precondition: a view that hides every colonist is in place (a query nobody matches, a status, a skill, a sort).
        // Trigger: save and load the game.
        // Failure mode: the static ViewState survives the load, so a fresh game shows an empty or filtered bar behind a
        // blank search field (the controls are rebuilt, the statics are not).
        private static void ViewResetOnLoad()
        {
            var s = new Scenario("View reset on load", 4000000);
            Roster r = Prelude(s, 5);
            s.OnFinish(() => ViewState.Reset());
            s.Do("set a query nobody matches, a status, a skill, a minimum level, a sort and a direction", () =>
                {
                    ViewState.Query = "zzznomatchzzz";
                    ViewState.Status = StatusFilter.Injured;
                    ViewState.Skill = DefDatabase<SkillDef>.AllDefsListForReading[0];
                    ViewState.MinLevel = 10;
                    ViewState.Sort = SortKey.Skill;
                    ViewState.Descending = true;
                })
                .ExpectWithin("the bar is empty behind them", 20, () => BarAdapter.Applied && BarProbe.CachedEntries(Bar).Count == 0 && !BarProbe.IsDirty(Bar), () => $"applied {BarAdapter.Applied}");
            s.SaveAndReload(always: true);
            s.ExpectWithin("a different game: the state is at its defaults and the bar is the vanilla roster again", 30, () =>
                {
                    if (ViewState.Query != "" || ViewState.Status != StatusFilter.None || ViewState.Skill != null
                        || ViewState.MinLevel != ViewState.AnyLevel || ViewState.Sort != SortKey.Vanilla || ViewState.Descending)
                    {
                        r.Last = $"state survived the load: query '{ViewState.Query}', {ViewState.Status}, {ViewState.Skill?.defName ?? "no skill"}, level {ViewState.MinLevel}, {ViewState.Sort}, descending {ViewState.Descending}";
                        return false;
                    }
                    return VanillaBack(r);
                }, () => r.Last)
                .Do("the new game's bar is untouched: nothing of ours is written", () =>
                {
                    if (BarAdapter.Applied)
                        throw new InvalidOperationException("the adapter still reports a written view after the load");
                    r.Notes.Add("after the load: defaults, vanilla roster and layout");
                })
                .Start();
        }
    }
}
